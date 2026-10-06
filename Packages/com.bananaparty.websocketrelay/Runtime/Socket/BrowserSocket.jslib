const browserSocketLibrary = {
    // Class definition.

    $browserSocket: {
        // Keyed by id so disposed sockets free their slot without shifting the others.
        sockets: {},
        nextSocketId: 1,

        // Matches the code browsers report for connections that end without a close frame.
        abnormalClosureCode: 1006,

        getSocket: function (socketId) {
            return browserSocket.sockets[socketId] || null;
        },

        getBrowserSocketIsConnected: function (socketId) {
            const socket = browserSocket.getSocket(socketId);
            return (
                !!socket &&
                !!socket.webSocket &&
                socket.webSocket.readyState === WebSocket.OPEN
            );
        },

        getBrowserSocketCloseCode: function (socketId) {
            const socket = browserSocket.getSocket(socketId);
            if (!socket) return browserSocket.abnormalClosureCode;
            if (socket.closeCode !== 0) return socket.closeCode;
            if (socket.webSocket.readyState === WebSocket.CLOSED)
                return browserSocket.abnormalClosureCode;
            return 0;
        },

        getBrowserSocketBufferedAmount: function (socketId) {
            const socket = browserSocket.getSocket(socketId);
            return socket && socket.webSocket
                ? socket.webSocket.bufferedAmount
                : 0;
        },

        getBrowserSocketHasUnreadPayloadQueue: function (socketId) {
            const socket = browserSocket.getSocket(socketId);
            return !!socket && socket.payloadQueue.length > 0;
        },

        browserSocketReadPayloadQueue: function (
            socketId,
            payloadBytesBufferPtr,
            payloadBytesBufferLength,
        ) {
            const socket = browserSocket.getSocket(socketId);
            if (!socket) {
                console.error("Socket with id " + socketId + " is missing");
                return 0;
            }

            if (socket.payloadQueue.length === 0) {
                return 0;
            }

            const payloadBytesCount = socket.payloadQueue[0].length;
            if (payloadBytesBufferLength < payloadBytesCount)
                return payloadBytesCount;

            const payloadBytes = socket.payloadQueue.shift();
            HEAPU8.set(payloadBytes, payloadBytesBufferPtr);
            return payloadBytesCount;
        },

        browserSocketConnect: function (serverAddress) {
            const socketId = browserSocket.nextSocketId++;
            const socket = {
                webSocket: null,
                payloadQueue: [],
                closeCode: 0,
            };
            browserSocket.sockets[socketId] = socket;

            let webSocket;
            try {
                webSocket = new WebSocket(serverAddress);
            } catch (error) {
                // Invalid addresses throw instead of firing onclose.
                console.error(
                    "WebSocket connection to " +
                        serverAddress +
                        " failed: " +
                        error,
                );
                socket.closeCode = browserSocket.abnormalClosureCode;
                return socketId;
            }

            webSocket.binaryType = "arraybuffer";

            webSocket.onmessage = function (messageEvent) {
                if (messageEvent.data instanceof ArrayBuffer) {
                    socket.payloadQueue.push(new Uint8Array(messageEvent.data));
                } else if (typeof messageEvent.data === "string") {
                    socket.payloadQueue.push(
                        new TextEncoder().encode(messageEvent.data),
                    );
                } else if (messageEvent.data instanceof Blob) {
                    console.error(
                        "Blob message type not supported. messageEvent.data=" +
                            messageEvent.data,
                    );
                } else {
                    console.error(
                        "Unknown message type not supported. messageEvent.data=" +
                            messageEvent.data,
                    );
                }
            };

            webSocket.onclose = function (closeEvent) {
                socket.closeCode =
                    closeEvent.code || browserSocket.abnormalClosureCode;
            };

            socket.webSocket = webSocket;
            return socketId;
        },

        browserSocketSend: function (socketId, payloadBytes) {
            if (!browserSocket.getBrowserSocketIsConnected(socketId)) return;
            browserSocket.sockets[socketId].webSocket.send(payloadBytes);
        },

        browserSocketDisconnect: function (socketId) {
            const socket = browserSocket.getSocket(socketId);
            if (!socket || !socket.webSocket) return;

            const readyState = socket.webSocket.readyState;
            if (
                readyState === WebSocket.CONNECTING ||
                readyState === WebSocket.OPEN
            )
                socket.webSocket.close();
        },

        browserSocketDispose: function (socketId) {
            const socket = browserSocket.getSocket(socketId);
            if (!socket) return;

            browserSocket.browserSocketDisconnect(socketId);
            if (socket.webSocket) {
                socket.webSocket.onmessage = null;
                socket.webSocket.onclose = null;
            }
            delete browserSocket.sockets[socketId];
        },
    },

    // External C# calls.

    GetBrowserSocketIsConnected: function (socketId) {
        return browserSocket.getBrowserSocketIsConnected(socketId);
    },

    GetBrowserSocketCloseCode: function (socketId) {
        return browserSocket.getBrowserSocketCloseCode(socketId);
    },

    GetBrowserSocketBufferedAmount: function (socketId) {
        return browserSocket.getBrowserSocketBufferedAmount(socketId);
    },

    GetBrowserSocketHasUnreadPayloadQueue: function (socketId) {
        return browserSocket.getBrowserSocketHasUnreadPayloadQueue(socketId);
    },

    BrowserSocketReadPayloadQueue: function (
        socketId,
        payloadBytesBufferPtr,
        payloadBytesBufferLength,
    ) {
        return browserSocket.browserSocketReadPayloadQueue(
            socketId,
            payloadBytesBufferPtr,
            payloadBytesBufferLength,
        );
    },

    BrowserSocketConnect: function (serverAddressPtr) {
        const serverAddress = UTF8ToString(serverAddressPtr);
        return browserSocket.browserSocketConnect(serverAddress);
    },

    BrowserSocketSend: function (socketId, payloadBytesPtr, payloadBytesCount) {
        const bytesToSend = HEAPU8.buffer.slice(
            payloadBytesPtr,
            payloadBytesPtr + payloadBytesCount,
        );
        browserSocket.browserSocketSend(socketId, bytesToSend);
    },

    BrowserSocketDisconnect: function (socketId) {
        browserSocket.browserSocketDisconnect(socketId);
    },

    BrowserSocketDispose: function (socketId) {
        browserSocket.browserSocketDispose(socketId);
    },
};

autoAddDeps(browserSocketLibrary, "$browserSocket");
mergeInto(LibraryManager.library, browserSocketLibrary);
