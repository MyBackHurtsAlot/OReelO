const sessionId = process.argv[2];
const expression = process.argv.slice(3).join(" ") || `JSON.stringify({helper:document.querySelector('#helper-status')?.textContent,addTemplate:!!document.querySelector('#add-template'),body:document.body.innerText})`;
const socket = new WebSocket(`ws://127.0.0.1:14001/socket/cdt/${sessionId}`);
let evaluated = false;
const timeout = setTimeout(() => process.exit(2), 8000);

socket.addEventListener("open", () => {
  socket.send(JSON.stringify({ id: 1, method: "Runtime.enable" }));
});

socket.addEventListener("message", event => {
  const message = JSON.parse(event.data);
  const context = message.params && message.params.context;
  if (!context || evaluated) return;
  evaluated = true;
  socket.send(JSON.stringify({
    id: 2,
    method: "Runtime.evaluate",
    params: {
      contextId: context.id,
      returnByValue: true,
      awaitPromise: true,
      expression
    }
  }));
  return;
});

socket.addEventListener("message", event => {
  const message = JSON.parse(event.data);
  if (message.id !== 2) return;
  clearTimeout(timeout);
  console.log(JSON.stringify(message));
  setTimeout(() => process.exit(0), 100);
});
