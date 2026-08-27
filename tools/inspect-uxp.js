const sessionId = process.argv[2];
const cli = new WebSocket("ws://127.0.0.1:14001/socket/cli");
let runtimeId;
let sent = false;

const timeout = setTimeout(() => process.exit(2), 8000);

cli.addEventListener("message", event => {
  const message = JSON.parse(event.data);
  if (message.command === "didAddRuntimeClient") runtimeId = message.id;
  if (message.command === "didCompleteConnection" && runtimeId && !sent) {
    sent = true;
    cli.send(JSON.stringify({
      command: "proxy",
      clientId: runtimeId,
      message: { command: "Plugin", action: "debug", pluginSessionId: sessionId },
      requestId: 2
    }));
  }
  if (message.requestId !== 2) return;
  clearTimeout(timeout);
  console.log(JSON.stringify(message));
  setTimeout(() => process.exit(0), 100);
});
