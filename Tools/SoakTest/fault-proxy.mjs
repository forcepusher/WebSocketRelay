import net from "node:net";
import { performance } from "node:perf_hooks";

/**
 * TCP proxy that degrades the link between one client and the relay server like a poor network does.
 * Data stays in order, as TCP keeps it, so latency, jitter, lost packets and slow links all show up as delay.
 * A full queue stops reading from the sender, which fills its socket buffers like a congested router would.
 */
export class FaultProxy {
    static cleanProfile = Object.freeze({
        latencyMs: 0,
        jitterMs: 0,
        // Chance per chunk of a hold, which is how a lost packet looks over TCP: everything waits for the retransmit.
        holdProbability: 0,
        holdMinMs: 0,
        holdMaxMs: 0,
        bandwidthBytesPerSecond: 0,
        queueLimitBytes: 64 * 1024,
    });

    #name;
    #listenPort;
    #targetPort;
    #server = null;
    #connections = new Set();
    #profile = { ...FaultProxy.cleanProfile };
    #isStalled = false;

    acceptedCount = 0;

    constructor({ name, listenPort, targetPort }) {
        this.#name = name;
        this.#listenPort = listenPort;
        this.#targetPort = targetPort;
    }

    get name() {
        return this.#name;
    }

    get port() {
        return this.#listenPort;
    }

    get profile() {
        return this.#profile;
    }

    get isStalled() {
        return this.#isStalled;
    }

    async start() {
        this.#server = net.createServer((client) => this.#accept(client));
        await new Promise((resolve, reject) => {
            this.#server.once("error", reject);
            this.#server.listen(this.#listenPort, "127.0.0.1", resolve);
        });
    }

    async stop() {
        this.resetAll();
        if (this.#server) await new Promise((resolve) => this.#server.close(resolve));
        this.#server = null;
    }

    setProfile(profile = {}) {
        this.#profile = { ...FaultProxy.cleanProfile, ...profile };
        for (const connection of this.#connections) connection.wake();
    }

    /** While stalled nothing moves in either direction, new connections included, and held data flows again afterwards. */
    setStalled(isStalled) {
        this.#isStalled = isStalled;
        for (const connection of this.#connections) connection.wake();
    }

    /** Resets every connection with RST, like a network change or a NAT timeout. */
    resetAll() {
        for (const connection of [...this.#connections]) connection.reset();
    }

    #accept(client) {
        this.acceptedCount++;
        const upstream = net.connect({ host: "127.0.0.1", port: this.#targetPort });
        const connection = new ProxiedConnection(this, client, upstream, () => this.#connections.delete(connection));
        this.#connections.add(connection);
    }
}

class ProxiedConnection {
    #proxy;
    #client;
    #upstream;
    #pipes;
    #onClosed;
    #isClosed = false;
    #isClosePending = false;

    constructor(proxy, client, upstream, onClosed) {
        this.#proxy = proxy;
        this.#client = client;
        this.#upstream = upstream;
        this.#onClosed = onClosed;
        client.setNoDelay(true);
        upstream.setNoDelay(true);
        this.#pipes = [new Pipe(proxy, client, upstream), new Pipe(proxy, upstream, client)];

        for (const socket of [client, upstream]) {
            socket.on("error", () => this.#close());
            socket.on("close", () => this.#close());
        }
    }

    wake() {
        if (this.#isClosePending && !this.#proxy.isStalled) {
            this.reset();
            return;
        }

        for (const pipe of this.#pipes) pipe.wake();
    }

    // A dead link loses the RST too, so the other side only learns about the closed connection once the link is back.
    #close() {
        if (this.#proxy.isStalled) this.#isClosePending = true;
        else this.reset();
    }

    reset() {
        if (this.#isClosed) return;
        this.#isClosed = true;
        for (const pipe of this.#pipes) pipe.dispose();
        for (const socket of [this.#client, this.#upstream]) {
            if (socket.destroyed) continue;
            if (typeof socket.resetAndDestroy === "function" && socket.readyState === "open") socket.resetAndDestroy();
            else socket.destroy();
        }
        this.#onClosed();
    }
}

class Pipe {
    #proxy;
    #source;
    #destination;
    #queue = [];
    #queuedBytes = 0;
    #lastReleaseTime = 0;
    #linkFreeTime = 0;
    #timer = null;
    #isDisposed = false;

    constructor(proxy, source, destination) {
        this.#proxy = proxy;
        this.#source = source;
        this.#destination = destination;
        source.on("data", (chunk) => this.#enqueue(chunk));
    }

    wake() {
        this.#clearTimer();
        this.#flush();
    }

    dispose() {
        this.#isDisposed = true;
        this.#clearTimer();
        this.#queue = [];
    }

    #enqueue(chunk) {
        const profile = this.#proxy.profile;
        const now = performance.now();
        let delay = profile.latencyMs + Math.random() * profile.jitterMs;
        if (profile.holdProbability > 0 && Math.random() < profile.holdProbability)
            delay += profile.holdMinMs + Math.random() * (profile.holdMaxMs - profile.holdMinMs);

        // Never earlier than the previous chunk, because TCP delivers in order.
        let releaseTime = Math.max(now + delay, this.#lastReleaseTime);
        if (profile.bandwidthBytesPerSecond > 0) {
            this.#linkFreeTime = Math.max(this.#linkFreeTime, releaseTime) + (chunk.length * 1000) / profile.bandwidthBytesPerSecond;
            releaseTime = this.#linkFreeTime;
        }

        this.#lastReleaseTime = releaseTime;
        this.#queue.push({ chunk, releaseTime });
        this.#queuedBytes += chunk.length;
        if (this.#queuedBytes > profile.queueLimitBytes) this.#source.pause();

        if (!this.#timer) this.#flush();
    }

    #flush() {
        this.#timer = null;
        if (this.#isDisposed) return;

        const now = performance.now();
        if (!this.#proxy.isStalled) {
            while (this.#queue.length > 0 && this.#queue[0].releaseTime <= now) {
                const { chunk } = this.#queue.shift();
                this.#queuedBytes -= chunk.length;
                if (!this.#destination.destroyed) this.#destination.write(chunk);
            }

            if (this.#queuedBytes <= this.#proxy.profile.queueLimitBytes / 2 && this.#source.isPaused()) this.#source.resume();
        }

        if (this.#queue.length === 0) return;

        const delay = this.#proxy.isStalled ? 20 : Math.max(0, this.#queue[0].releaseTime - now);
        this.#timer = setTimeout(() => this.#flush(), delay);
    }

    #clearTimer() {
        if (!this.#timer) return;
        clearTimeout(this.#timer);
        this.#timer = null;
    }
}
