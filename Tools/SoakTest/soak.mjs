// Multi-process reliability run: a real relay server, several soak client processes, and a fault proxy per client.
//
// node soak.mjs --client <SoakClient.exe> --relayDir <RelayServer~ folder> --out <results folder> [--label name]
//               [--clients 6] [--binary] [--webgl <WebGL build folder> --webglClients 1] [--quick]
//
// Clients report once per second to the collector, which answers with commands such as "hitch 3000".
// Every phase degrades the network or the clients in one way and checks how the session copes.

import { spawn, execFileSync } from "node:child_process";
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { FaultProxy } from "./fault-proxy.mjs";

const options = parseArguments(process.argv.slice(2));
const clientCount = Number(options.clients ?? 6);
const webglClientCount = options.webgl ? Number(options.webglClients ?? 1) : 0;
const totalClients = clientCount + webglClientCount;
const relayPort = Number(options.relayPort ?? 23300);
const collectorPort = Number(options.collectorPort ?? 23380);
const outputDirectory = path.resolve(options.out ?? "soak-results");
const label = options.label ?? path.basename(outputDirectory);
const slowClientIndex = clientCount - 1;

fs.mkdirSync(outputDirectory, { recursive: true });

const proxies = [];
const clientProcesses = [];
const reports = Array.from({ length: totalClients }, () => []);
const finals = new Array(totalClients).fill(null);
const commandQueues = Array.from({ length: totalClients }, () => []);
const timeline = [];
const relayCrashes = [];
let relayProcess = null;
let currentPhase = "startup";
const runStart = Date.now();

const phases = buildPhases();

main().catch((error) => {
    console.error(error);
    shutdown().finally(() => process.exit(1));
});

async function main() {
    log(`soak run "${label}": ${clientCount} native + ${webglClientCount} WebGL clients, binary=${!!options.binary}`);
    await startRelay();
    for (let index = 0; index < totalClients; index++) {
        const proxy = new FaultProxy({ name: `client${index}`, listenPort: relayPort + 1 + index, targetPort: relayPort });
        await proxy.start();
        proxies.push(proxy);
    }

    const collector = await startCollector();
    for (let index = 0; index < clientCount; index++) launchClient(index);
    if (webglClientCount > 0) log(`open WebGL clients: ${webglUrls().join("  ")}`);

    await waitFor(() => reports.every((clientReports) => clientReports.some((report) => report.state === "Connected")), 60_000, "every client to connect");

    for (const phase of phases) {
        currentPhase = phase.name;
        const phaseStart = Date.now();
        timeline.push({ name: phase.name, start: phaseStart - runStart });
        log(`phase ${phase.name}: ${phase.description}`);
        await phase.run?.();
        const remaining = phase.seconds * 1000 - (Date.now() - phaseStart);
        if (remaining > 0) await sleep(remaining);
        await phase.cleanup?.();
        timeline[timeline.length - 1].end = Date.now() - runStart;
    }

    currentPhase = "shutdown";
    for (let index = 0; index < totalClients; index++) commandQueues[index].push("quit");
    await waitFor(() => finals.slice(0, clientCount).every((final) => final !== null), 15_000, "final reports").catch(() => log("some final reports are missing"));
    await shutdown();
    collector.close();

    const analysis = analyze();
    fs.writeFileSync(path.join(outputDirectory, "reports.json"), JSON.stringify({ label, options, timeline, reports, finals }, null, 1));
    fs.writeFileSync(path.join(outputDirectory, "analysis.json"), JSON.stringify(analysis, null, 2));
    fs.writeFileSync(path.join(outputDirectory, "summary.md"), renderSummary(analysis));
    log(`results written to ${outputDirectory}`);
    console.log(renderSummary(analysis));
}

function buildPhases() {
    const quick = !!options.quick;
    const scale = (seconds) => (quick ? Math.max(8, Math.round(seconds / 2)) : seconds);
    // The phases are built before the proxies exist, so this counts clients instead of proxies.
    const all = () => Array.from({ length: totalClients }, (_, index) => index);
    return [
        { name: "warmup", seconds: scale(20), description: "clean network", expect: {} },
        {
            name: "latency-jitter",
            seconds: scale(25),
            description: "client 1: 150 ms latency with up to 100 ms jitter each way",
            run: () => proxies[1].setProfile({ latencyMs: 150, jitterMs: 100 }),
            cleanup: () => proxies[1].setProfile(),
            expect: {},
        },
        {
            name: "packet-loss",
            seconds: scale(25),
            description: "client 2: 80 ms latency, 3% of chunks held 0.3 to 1.2 s like retransmitted packets",
            run: () => proxies[2].setProfile({ latencyMs: 80, holdProbability: 0.03, holdMinMs: 300, holdMaxMs: 1200 }),
            cleanup: () => proxies[2].setProfile(),
            expect: {},
        },
        {
            name: "slow-link",
            seconds: scale(25),
            description: "client 3: 16 KB/s each way, less than the session needs",
            run: () => proxies[3].setProfile({ bandwidthBytesPerSecond: 16 * 1024, queueLimitBytes: 32 * 1024 }),
            cleanup: () => proxies[3].setProfile(),
            expect: { tolerateReconnects: [3] },
        },
        {
            name: "short-stall",
            seconds: scale(20),
            description: "client 4: link stalls for 3 s, shorter than the heartbeat timeout",
            run: () => stallFor(4, 3000),
            expect: { reconnects: { 4: 0 } },
        },
        {
            name: "long-stall",
            seconds: scale(25),
            description: "client 1: link stalls for 8 s, longer than the heartbeat timeout but shorter than the player timeout",
            run: () => stallFor(1, 8000),
            expect: { reconnects: { 1: 1 } },
        },
        {
            name: "blackout",
            seconds: scale(35),
            description: "client 2: link stalls for 16 s, longer than the player timeout, so peers drop it until it is back",
            run: () => stallFor(2, 16000),
            expect: { reconnects: { 2: 1 }, peersMayDrop: [2] },
        },
        {
            name: "reset-all",
            seconds: scale(20),
            description: "every connection is reset at once, like a NAT or Wi-Fi change",
            run: () => all().forEach((index) => proxies[index].resetAll()),
            expect: { reconnects: Object.fromEntries(all().map((index) => [index, 1])) },
        },
        {
            name: "relay-restart",
            seconds: scale(30),
            description: "relay server is killed and started again 4 s later",
            run: async () => {
                await stopRelay();
                await sleep(4000);
                await startRelay();
            },
            expect: { reconnects: Object.fromEntries(all().map((index) => [index, 1])) },
        },
        {
            name: "short-hitch",
            seconds: scale(20),
            description: "client 3: main thread freezes for 3 s, like loading a level",
            run: () => commandQueues[3].push("hitch 3000"),
            expect: { reconnects: { 3: 0 } },
        },
        {
            name: "long-hitch",
            seconds: scale(25),
            description: "client 0: main thread freezes for 7 s, like an app in the background",
            run: () => commandQueues[0].push("hitch 7000"),
            expect: { reconnects: { 0: 1 } },
        },
        {
            name: "rpc-burst",
            seconds: scale(20),
            description: "every client sends 200 RPCs in one frame",
            run: () => all().forEach((index) => commandQueues[index].push("burst 200")),
            expect: {},
        },
        {
            name: "high-rate",
            seconds: scale(25),
            description: "every client sends 30 RPCs per second instead of 5",
            run: () => all().forEach((index) => commandQueues[index].push("rate 30")),
            cleanup: () => all().forEach((index) => commandQueues[index].push("rate 5")),
            expect: {},
        },
        {
            name: "cooldown",
            seconds: scale(30),
            description: "clean network and avatars stop moving, so every client must end up agreeing on the owners",
            run: () => all().forEach((index) => commandQueues[index].push("freeze")),
            expect: { ownersAgree: true },
        },
    ];
}

async function stallFor(index, milliseconds) {
    proxies[index].setStalled(true);
    await sleep(milliseconds);
    proxies[index].setStalled(false);
}

// Processes

// The relay server is restarted when it dies unexpectedly, like a process supervisor would do,
// so a crash shows up as a relay restart for the clients and is counted in the results.
async function startRelay() {
    const relayDirectory = path.resolve(options.relayDir);
    const bunPath = path.join(relayDirectory, "Bun", "bun-windows-x64", "bun.exe");
    const child = spawn(bunPath, ["Source/index.ts", "-relay-server"], {
        cwd: relayDirectory,
        env: { ...process.env, RELAY_PORT: String(relayPort), RELAY_DEBUG: "0", BUN_ENABLE_CRASH_REPORTING: "0" },
        stdio: ["ignore", "pipe", "pipe"],
    });
    relayProcess = child;
    const relayLog = fs.createWriteStream(path.join(outputDirectory, "relay.log"), { flags: "a" });
    child.stdout.pipe(relayLog);
    child.stderr.pipe(relayLog);
    child.on("exit", (code) => {
        if (child !== relayProcess || child.isStopping) return;
        relayProcess = null;
        relayCrashes.push({ at: Date.now() - runStart, phase: currentPhase, code });
        log(`relay server died with exit code ${code} during ${currentPhase}, restarting it`);
        setTimeout(() => startRelay().catch((error) => log(`relay restart failed: ${error.message}`)), 1000);
    });
    await waitFor(() => canConnect(relayPort), 15_000, "relay server to listen");
}

async function stopRelay() {
    const child = relayProcess;
    if (!child) return;
    child.isStopping = true;
    relayProcess = null;
    if (child.exitCode !== null || child.signalCode !== null) return;
    const exited = new Promise((resolve) => child.once("exit", resolve));
    child.kill("SIGKILL");
    await exited;
}

function launchClient(index) {
    const arguments_ = [
        "-batchmode",
        "-nographics",
        "-logFile",
        path.join(outputDirectory, `client${index}.log`),
        "-soakRelay",
        `ws://127.0.0.1:${proxies[index].port}`,
        "-soakCollector",
        `http://127.0.0.1:${collectorPort}`,
        "-soakIndex",
        String(index),
        "-soakFps",
        String(index === slowClientIndex ? 30 : 60),
        "-soakBinary",
        options.binary ? "1" : "0",
    ];
    const child = spawn(path.resolve(options.client), arguments_, { stdio: "ignore" });
    child.on("exit", (code) => log(`client ${index} exited with ${code}`));
    clientProcesses.push(child);
}

function webglUrls() {
    return Array.from({ length: webglClientCount }, (_, offset) => {
        const index = clientCount + offset;
        const query = new URLSearchParams({
            soakRelay: `ws://127.0.0.1:${proxies[index].port}`,
            soakCollector: `http://127.0.0.1:${collectorPort}`,
            soakIndex: String(index),
            soakBinary: options.binary ? "1" : "0",
        });
        return `http://127.0.0.1:${collectorPort}/index.html?${query}`;
    });
}

async function shutdown() {
    await sleep(500);
    for (const child of clientProcesses) {
        if (child.exitCode === null) {
            try {
                execFileSync("taskkill", ["/F", "/PID", String(child.pid)], { stdio: "ignore" });
            } catch {
                // Already gone.
            }
        }
    }
    for (const proxy of proxies) await proxy.stop();
    await stopRelay();
}

// Collector

function startCollector() {
    const server = http.createServer((request, response) => {
        const url = new URL(request.url, `http://127.0.0.1:${collectorPort}`);
        if (request.method === "POST" && url.pathname === "/report") {
            let body = "";
            request.on("data", (chunk) => (body += chunk));
            request.on("end", () => {
                const index = Number(url.searchParams.get("index"));
                try {
                    const report = JSON.parse(body);
                    report.phase = currentPhase;
                    report.receivedAt = Date.now() - runStart;
                    if (report.type === "final") finals[index] = report;
                    else reports[index].push(report);
                } catch (error) {
                    log(`bad report from client ${index}: ${error.message}`);
                }
                const commands = commandQueues[index] ?? [];
                response.writeHead(200, { "Content-Type": "text/plain", "Access-Control-Allow-Origin": "*" });
                response.end(commands.splice(0).join("\n"));
            });
            return;
        }

        serveStatic(url.pathname, response);
    });

    return new Promise((resolve) => server.listen(collectorPort, "127.0.0.1", () => resolve(server)));
}

function serveStatic(pathname, response) {
    if (!options.webgl) {
        response.writeHead(404).end();
        return;
    }

    const root = path.resolve(options.webgl);
    const filePath = path.join(root, decodeURIComponent(pathname === "/" ? "/index.html" : pathname));
    if (!filePath.startsWith(root) || !fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
        response.writeHead(404).end();
        return;
    }

    const types = { ".html": "text/html", ".js": "application/javascript", ".wasm": "application/wasm", ".data": "application/octet-stream", ".css": "text/css", ".png": "image/png", ".ico": "image/x-icon", ".json": "application/json" };
    response.writeHead(200, { "Content-Type": types[path.extname(filePath)] ?? "application/octet-stream" });
    fs.createReadStream(filePath).pipe(response);
}

// Analysis

function analyze() {
    const phaseResults = timeline.map((phase) => analyzePhase(phase, phases.find((candidate) => candidate.name === phase.name)));
    const totals = finals.map((final, index) => (final ? summarizeFinal(final, index) : { index, missing: true }));
    const failures = phaseResults.flatMap((phase) => phase.failures.map((failure) => `${phase.name}: ${failure}`));
    for (const total of totals) {
        if (total.missing) failures.push(`client ${total.index}: no final report`);
        else {
            if (total.duplicates > 0) failures.push(`client ${total.index}: ${total.duplicates} duplicate RPCs over the run`);
            if (total.exceptions > 0 || total.errors > 0) failures.push(`client ${total.index}: ${total.exceptions} exceptions and ${total.errors} errors logged`);
        }
    }

    return {
        label,
        clients: totalClients,
        binary: !!options.binary,
        durationSeconds: Math.round((Date.now() - runStart) / 1000),
        relayCrashes,
        phases: phaseResults,
        totals,
        failures,
    };
}

function analyzePhase(phase, definition) {
    const expect = definition?.expect ?? {};
    const failures = [];
    const crashesInPhase = relayCrashes.filter((crash) => crash.at >= phase.start && crash.at <= phase.end);
    const clients = reports.map((clientReports, index) => {
        const inPhase = clientReports.filter((report) => report.receivedAt >= phase.start && report.receivedAt <= phase.end);
        const before = clientReports.filter((report) => report.receivedAt < phase.start).at(-1);
        const last = inPhase.at(-1);
        const senders = inPhase.flatMap((report) => Object.values(report.senders ?? {}));
        return {
            index,
            reports: inPhase.length,
            fps: average(inPhase.map((report) => report.frames)),
            reconnects: (last?.reconnecting ?? 0) - (before?.reconnecting ?? 0),
            gaveUp: (last?.gaveUp ?? 0) - (before?.gaveUp ?? 0),
            minPlayers: Math.min(...inPhase.map((report) => report.players)),
            endState: last?.state,
            endPlayers: last?.players,
            endRemoteAvatars: last?.remoteAvatars?.length,
            duplicateAvatars: Math.max(0, ...inPhase.map((report) => report.duplicateAvatars)),
            maxStateLatency: Math.max(0, ...senders.map((sender) => sender.stateLatencyMax)),
            maxRpcLatency: Math.max(0, ...senders.map((sender) => sender.rpcLatencyMax)),
            rpcGaps: sum(senders.map((sender) => sender.rpcGaps)),
            rpcDuplicates: sum(senders.map((sender) => sender.rpcDuplicates)),
            maxPendingBytes: Math.max(0, ...inPhase.map((report) => report.pending)),
            maxRtt: Math.max(0, ...inPhase.map((report) => report.rtt)),
            exceptions: (last?.exceptions ?? 0) - (before?.exceptions ?? 0),
            errors: (last?.errors ?? 0) - (before?.errors ?? 0),
            events: inPhase.flatMap((report) => report.events ?? []),
            errorSamples: inPhase.flatMap((report) => report.errorSamples ?? []).slice(0, 5),
            shared: last?.shared,
        };
    });

    const others = totalClients - 1;
    if (crashesInPhase.length > 0) failures.push(`relay server crashed ${crashesInPhase.length} times and was restarted, so reconnect counts are not checked`);
    for (const client of clients) {
        const expectedReconnects = expect.reconnects?.[client.index] ?? 0;
        const tolerated = expect.tolerateReconnects?.includes(client.index) || crashesInPhase.length > 0;
        if (!tolerated && client.reconnects !== expectedReconnects)
            failures.push(`client ${client.index} reconnected ${client.reconnects} times, expected ${expectedReconnects}`);
        if (client.gaveUp > 0) failures.push(`client ${client.index} gave up reconnecting`);
        if (client.endState !== "Connected") failures.push(`client ${client.index} ended the phase ${client.endState}`);
        if (client.endPlayers !== others) failures.push(`client ${client.index} sees ${client.endPlayers} players at the end, expected ${others}`);
        if (client.endRemoteAvatars !== others) failures.push(`client ${client.index} sees ${client.endRemoteAvatars} remote avatars at the end, expected ${others}`);
        if (client.duplicateAvatars > 0) failures.push(`client ${client.index} had ${client.duplicateAvatars} duplicate avatars`);
        if (client.rpcDuplicates > 0) failures.push(`client ${client.index} received ${client.rpcDuplicates} duplicate RPCs`);
        if (client.exceptions > 0) failures.push(`client ${client.index} logged ${client.exceptions} exceptions: ${client.errorSamples.join(" | ")}`);

        // Clients that lose their own link must keep their peers, the others may drop only those clients.
        const mayDrop = expect.peersMayDrop ?? [];
        const allowedDrops = mayDrop.includes(client.index) ? 0 : mayDrop.length;
        if (client.minPlayers < others - allowedDrops && !tolerated)
            failures.push(`client ${client.index} dropped a peer during the phase (saw ${client.minPlayers} of ${others})`);
    }

    // While avatars move, owners legitimately change between two clients reporting, so agreement is only required once they stop.
    const ownership = sharedOwnershipAgreement(clients);
    if (expect.ownersAgree && !ownership.agreed) failures.push(`clients disagree on shared object owners at the end: ${ownership.detail}`);

    return {
        name: phase.name,
        description: definition?.description,
        seconds: Math.round((phase.end - phase.start) / 1000),
        relayCrashes: crashesInPhase.length,
        clients,
        ownership,
        failures,
    };
}

function sharedOwnershipAgreement(clients) {
    const views = clients.filter((client) => client.shared).map((client) => client.shared.map((shared) => (shared ? `${shared.owner}@${shared.version}` : "missing")));
    if (views.length === 0) return { agreed: false, detail: "no reports" };
    const reference = JSON.stringify(views[0]);
    const agreed = views.every((view) => JSON.stringify(view) === reference);
    return { agreed, detail: agreed ? reference : views.map((view, index) => `c${index}=${view.join(",")}`).join(" ") };
}

function summarizeFinal(final, index) {
    const senders = Object.values(final.senders ?? {});
    return {
        index,
        rpcSent: final.rpcSent,
        rpcsReceived: sum(senders.map((sender) => sender.totalRpcs)),
        gaps: sum(senders.map((sender) => sender.totalRpcGaps)),
        duplicates: sum(senders.map((sender) => sender.totalRpcDuplicates)),
        maxStateLatency: Math.max(0, ...senders.map((sender) => sender.totalStateLatencyMax)),
        maxRpcLatency: Math.max(0, ...senders.map((sender) => sender.totalRpcLatencyMax)),
        reconnecting: final.reconnecting,
        gaveUp: final.gaveUp,
        exceptions: final.exceptions,
        errors: final.errors,
        warnings: final.warnings,
    };
}

function renderSummary(analysis) {
    const lines = [];
    lines.push(`# Soak run: ${analysis.label}`, "");
    lines.push(`${analysis.clients} clients (client ${slowClientIndex} at 30 fps, the rest at 60 fps), ${analysis.binary ? "binary" : "JSON"} state, ${analysis.durationSeconds} s.`, "");
    lines.push(`Relay server crashes, restarted by the harness: ${analysis.relayCrashes.length}${analysis.relayCrashes.length ? ` (${analysis.relayCrashes.map((crash) => `${(crash.at / 1000).toFixed(0)} s in ${crash.phase}`).join(", ")})` : ""}.`, "");
    lines.push(analysis.failures.length === 0 ? "**All checks passed.**" : `**${analysis.failures.length} checks failed.**`, "");
    for (const failure of analysis.failures) lines.push(`- ${failure}`);
    lines.push("");
    lines.push("| Phase | Reconnects per client | Min players seen | Max state latency (ms) | Max RPC latency (ms) | RPC gaps | Max pending send (KB) | Owners agree | Relay crashes | Result |");
    lines.push("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
    for (const phase of analysis.phases) {
        const column = (selector) => phase.clients.map(selector).join(" / ");
        lines.push(
            `| ${phase.name} | ${column((client) => client.reconnects)} | ${column((client) => client.minPlayers)} | ${Math.max(...phase.clients.map((client) => client.maxStateLatency))} | ${Math.max(...phase.clients.map((client) => client.maxRpcLatency))} | ${sum(phase.clients.map((client) => client.rpcGaps))} | ${Math.round(Math.max(...phase.clients.map((client) => client.maxPendingBytes)) / 1024)} | ${phase.ownership.agreed ? "yes" : "no"} | ${phase.relayCrashes} | ${phase.failures.length === 0 ? "pass" : "FAIL"} |`,
        );
    }
    lines.push("", "| Client | RPCs sent | RPCs received | Gaps | Duplicates | Max state latency (ms) | Max RPC latency (ms) | Reconnects | Gave up | Exceptions | Errors |");
    lines.push("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
    for (const total of analysis.totals) {
        if (total.missing) lines.push(`| ${total.index} | missing | | | | | | | | | |`);
        else lines.push(`| ${total.index} | ${total.rpcSent} | ${total.rpcsReceived} | ${total.gaps} | ${total.duplicates} | ${total.maxStateLatency} | ${total.maxRpcLatency} | ${total.reconnecting} | ${total.gaveUp} | ${total.exceptions} | ${total.errors} |`);
    }
    lines.push("");
    return lines.join("\n");
}

// Helpers

function parseArguments(argv) {
    const parsed = {};
    for (let index = 0; index < argv.length; index++) {
        if (!argv[index].startsWith("--")) continue;
        const key = argv[index].slice(2);
        const value = argv[index + 1] && !argv[index + 1].startsWith("--") ? argv[++index] : true;
        parsed[key] = value;
    }
    if (!parsed.client || !parsed.relayDir) {
        console.error("usage: node soak.mjs --client <SoakClient.exe> --relayDir <RelayServer~> --out <folder> [--clients 6] [--binary] [--quick]");
        process.exit(2);
    }
    return parsed;
}

function canConnect(port) {
    return new Promise((resolve) => {
        const request = http.get({ host: "127.0.0.1", port, path: "/", timeout: 500 }, (response) => {
            response.resume();
            resolve(true);
        });
        request.on("error", () => resolve(false));
        request.on("timeout", () => {
            request.destroy();
            resolve(false);
        });
    });
}

async function waitFor(condition, timeoutMilliseconds, description) {
    const deadline = Date.now() + timeoutMilliseconds;
    while (!(await condition())) {
        if (Date.now() > deadline) throw new Error(`timed out waiting for ${description}`);
        await sleep(200);
    }
}

function sleep(milliseconds) {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function sum(values) {
    return values.reduce((total, value) => total + (value ?? 0), 0);
}

function average(values) {
    return values.length === 0 ? 0 : Math.round(sum(values) / values.length);
}

function log(message) {
    const line = `[${((Date.now() - runStart) / 1000).toFixed(1).padStart(6)}s] ${message}`;
    console.log(line);
    fs.appendFileSync(path.join(outputDirectory, "soak.log"), line + "\n");
}
