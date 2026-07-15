// See
// - https://chromedevtools.github.io/devtools-protocol/
// - https://chromedevtools.github.io/devtools-protocol/1-3/
// - https://github.com/ChromeDevTools/devtools-protocol/ for the types

/**
 * @typedef ProtocolApiMap
 * @property {import("./protocol-proxy-api").ProtocolProxyApi.DebuggerApi} Debugger
 * @property {import("./protocol-proxy-api").ProtocolProxyApi.RuntimeApi} Runtime
 */

/**
 * @typedef DebugProtocolRequest
 * @property {number} id
 * @property {string} method
 * @property {unknown} params
 */

/**
 * @typedef PendingDebugProtocolResponse
 * @property {number} id
 * @property {(response: unknown) => void} resolve
 * @property {() => void} reject
 */

/**
 * @typedef DebugProtocolResponseSuccess
 * @property {number} id
 * @property {unknown} result
 * 
 * @typedef DebugProtocolResponseError
 * @property {number} id
 * @property {{ code: number; message: string }} error
 * 
 * @typedef {DebugProtocolResponseSuccess | DebugProtocolResponseError} DebugProtocolResponse
 */

/**
 * @typedef DebugProtocolNotification
 * @property {string} method
 * @property {unknown} params
 */

/**
 * @typedef {DebugProtocolResponse | DebugProtocolNotification} DebugProtocolMessage
 */

/**
 * @typedef Transport
 * @property {(message: string) => void} sendMessage
 * @property {(listener: (message: string) => void) => void} onDidReceiveMessage
 * @property {(listener: (reason: "close" | "error", error?: any) => void) => void} onDidTerminate
 * @property {() => void} disconnect
 */

class WebsocketTransport {
    /**
     * @type {WebSocket}
     * @readonly
     * @private
     */
    ws;

    /** 
     * @type {Promise<void>}
     * @readonly
     * @private
     */
    openBarrier;

    /**
     * @param {string} address
     */
    constructor(address) {
        this.ws = new WebSocket(address);
        this.openBarrier = new Promise(resolve => this.ws.addEventListener("open", () => resolve()));
    }

    /**
     * @param {string} data
     */
    sendMessage(data) {
        this.openBarrier.then(() => {
            if (this.ws.readyState === this.ws.OPEN) {
                this.ws.send(data);
            }
        });
    }

    /**
     * @param {(message: string) => void} listener
     */
    onDidReceiveMessage(listener) {
        this.ws.addEventListener("message", ev => listener(ev.data));
    }

    /**
     * @param {(reason: "close" | "error", error?: any) => void} listener
     */
    onDidTerminate(listener) {
        this.ws.addEventListener("close", () => listener("close"));
        this.ws.addEventListener("error", ev => listener("error", ev));
    }

    disconnect() {
        this.ws.close();
    }
}

class DebugSession {
    /**
     * @private
     */
    messageId = 1;

    /**
     * @type {Transport}
     * @readonly
     */
    transport;

    /**
     * @type {Map<string, (params: unknown) => void>}
     * @readonly
     * @private
     */
    notificationListeners = new Map();

    /**
     * @type {Map<number, PendingDebugProtocolResponse>}
     * @readonly
     * @private
     */
    pendingResponses = new Map(); // TODO(seb) Can this be a fixed sized array that we wrap around?

    /**
     * @param {Transport} transport
     */
    constructor(transport) {
        this.transport = transport;
        transport.onDidReceiveMessage(message => this.onDidReceiveMessage(message));
    }

    /**
     * @param {string} data
     * @private
     */
    onDidReceiveMessage(data) {
        /** @type {DebugProtocolMessage} */
        const message = JSON.parse(data);
        if ("id" in message) {
            const pendingResponse = this.pendingResponses.get(message.id);
            if (pendingResponse && pendingResponse.id === message.id) {
                this.pendingResponses.delete(message.id);
                if ("error" in message) {
                    console.error(message.error);
                    pendingResponse.reject();
                } else {
                    pendingResponse.resolve(message.result);
                }
            } else {
                console.error(`No or wrong pending response found for id ${message.id}`, pendingResponse);
            }
        } else {
            this.notificationListeners.get(message.method)?.(message.params);
        }
    }

    /**
     * @param {string} method
     * @param {unknown} params
     * @returns {Promise<unknown>}
     * @private
     */
    sendRequest(method, params) {
        /** @type {DebugProtocolRequest} */
        const request = {
            id: this.messageId++,
            method,
            params,
        };
        const pendingResponse = new Promise((resolve, reject) => this.pendingResponses.set(request.id, { id: request.id, resolve, reject }));
        this.transport.sendMessage(JSON.stringify(request));
        return pendingResponse;
    }

    debugger = this.createProxy("Debugger");
    runtime = this.createProxy("Runtime");

    /**
     * @template {keyof ProtocolApiMap} T
     * @param {T} api
     * @returns {ProtocolApiMap[T]}
     * @private
     */
    createProxy(api) {
        //@ts-ignore
        return new Proxy(this, {
            get(target, prop, receiver) {
                if (prop === "on") {
                    return (/** @type {string} */ event, /** @type {(params: unknown) => void} */ listener) => target.notificationListeners.set(api + "." + event, listener);
                } else if (typeof prop === "string") {
                    return (/** @type {unknown} */ params) => target.sendRequest(api + "." + prop, params);
                } else {
                    return undefined;
                }
            },
        });
    }
}

async function main() {
    /** @type {HTMLInputElement} */
    const addressInput = document.querySelector("#address");
    /** @type {HTMLButtonElement} */
    const startAndContinueButton = document.querySelector("#start-and-continue");
    /** @type {HTMLButtonElement} */
    const stopButton = document.querySelector("#stop");
    /** @type {HTMLButtonElement} */
    const stepOverButton = document.querySelector("#step-over");

    const enableButtons = (/** @type {boolean} */ enable) => {
        startAndContinueButton.disabled = !enable;
        stopButton.disabled = !enable;
        stepOverButton.disabled = !enable;
    }

    let terminated = true;
    /** @type {DebugSession} */
    let session;

    startAndContinueButton.addEventListener("click", async () => {
        if (terminated) {
            session = new DebugSession(new WebsocketTransport(addressInput.value));
            session.transport.onDidTerminate((reason, error) => {
                if (reason === "close") {
                    console.log("Transport connection was closed");
                } else {
                    console.error("Transport connection was closed unexpectedly", error);
                }
                terminated = true;
                startAndContinueButton.textContent = "Start";
                startAndContinueButton.disabled = false;
                stopButton.disabled = true;
                stepOverButton.disabled = true;
            });
            session.runtime.on("executionContextCreated", params => {
                console.log("Debugging session started", params);
            });
            session.runtime.on("executionContextDestroyed", params => {
                console.log("Debugging session finished", params);
                session.transport.disconnect();
            });
            session.runtime.on("consoleAPICalled", params => {
                const args = params.args.map(arg => {
                    switch (arg.type) {
                        case "bigint": return BigInt(arg.unserializableValue.slice(0, -1));
                        case "function": return arg.description ?? "<function>";
                        case "object": {
                            if (arg.subtype === "null") {
                                return null;
                            } else if (arg.preview) {
                                if (arg.subtype) {
                                    return arg.description;
                                } else {
                                    const obj = Object.create(null);
                                    for (const prop of arg.preview.properties) {
                                        let value;
                                        switch (prop.type) {
                                            case "bigint": value = BigInt(prop.value.slice(0, -1)); break;
                                            case "boolean": value = Boolean(prop.value); break;
                                            case "number": value = Number(prop.value); break;
                                            default: value = prop.value; break;
                                        }
                                        obj[prop.name] = value;
                                    }
                                    return obj;
                                }
                            } else {
                                return "<object>";
                            }
                        }
                        case "symbol": return arg.description ?? "<symbol>";
                        default: return arg.value;
                    }
                });
                switch (params.type) {
                    case "assert": console.assert(...args); break;
                    case "warning": console.warn(...args); break;
                    case "startGroup": console.group(...args); break;
                    case "startGroupCollapsed": console.groupCollapsed(...args); break;
                    case "endGroup": console.groupEnd(); break;
                    case "profile": console.time(...args); break;
                    case "profileEnd": console.timeEnd(...args); break;
                    default: console[params.type](...args); break;
                }
            })
            session.debugger.on("scriptParsed", async params => {
                console.log("scriptParsed notification", params);
                const source = await session.debugger.getScriptSource({ scriptId: params.scriptId });
                console.log("script src result", source);
                const breakpointLocations = await session.debugger.getPossibleBreakpoints({ start: { scriptId: params.scriptId, lineNumber: 0 } });
                console.log("possible breakpoint locations", breakpointLocations);
            });
            session.debugger.on("paused", params => {
                console.log("paused notification", params);
                startAndContinueButton.textContent = "Continue";
                enableButtons(true);
            });
            await session.runtime.enable();
            console.log("Runtime.enable done");
            const enableResult = await session.debugger.enable({});
            console.log("Debugger.enable response", enableResult);
            await session.debugger.pause(); // pause on first statement
            console.log("Scheduled pause on first statement");
            await session.runtime.runIfWaitingForDebugger();
            terminated = false;
        } else {
            enableButtons(false);
            session.debugger.resume({});
        }
    });
    stopButton.addEventListener("click", () => {
        enableButtons(false);
        session.debugger.resume({ terminateOnResume: true });
    });
    stepOverButton.addEventListener("click", () => {
        enableButtons(false);
        session.debugger.stepOver({});
    });
}

main();