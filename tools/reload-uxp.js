const pluginPath = process.argv[2];
const socket = new WebSocket("ws://127.0.0.1:14001/socket/cli");
let runtimeClientId = null;
let sent = false;

const timeout = setTimeout(() => {
  console.error("UXP reload timed out");
  process.exit(2);
}, 10000);

socket.addEventListener("message", event => {
  const response = JSON.parse(event.data);
  console.log(JSON.stringify(response));

  if (response.command === "didAddRuntimeClient") {
    runtimeClientId = response.id;
  }

  if (response.command === "didCompleteConnection" && runtimeClientId && !sent) {
    sent = true;
    socket.send(JSON.stringify({
      command: "proxy",
      clientId: runtimeClientId,
      message: {
        command: "Plugin",
        action: "load",
        params: {
          provider: { type: "disk", path: pluginPath }
        },
        breakOnStart: false
      },
      requestId: 1
    }));
  }

  if (response.requestId === 1) {
    clearTimeout(timeout);
    setTimeout(() => process.exit(0), 200);
  }
});

socket.addEventListener("error", () => {
  console.error("Cannot connect to UXP Developer Tool on port 14001");
  process.exit(1);
});
