const geometry = globalThis.SequenceWheelGeometry;
const HOLD_MS = 190;
const DEAD_ZONE = 42;
const MAX_ITEMS = 10;
const HELPER_URL = "http://127.0.0.1:17321";
const BUILD_VERSION = "0.6.0";
const PALETTE_COLORS = [
  "#374151", "#64748b", "#2563eb", "#0891b2", "#0f766e", "#16a34a",
  "#65a30d", "#ca8a04", "#ea580c", "#dc2626", "#db2777", "#9333ea"
];
const DEFAULT_COLORS = PALETTE_COLORS.slice(0, 6);

let premiere = null;
let project = null;
let known = [];
let activeGuid = null;
let holdTimer = null;
let wheelOrigin = null;
let selectedIndex = -1;
let pollTimer = null;
let holdingRightButton = false;
let awaitingFallbackClick = false;
let helperPollTimer = null;
let helperRequestRunning = false;
let sequenceColors = loadSequenceColors();
let templates = loadTemplates();
let localFileSystem = null;
let mogrtRoot = "";
let installedTemplates = [];

const pad = document.querySelector("#pad");
const wheel = document.querySelector("#wheel");
const wheelItems = document.querySelector("#wheel-items");
const knownList = document.querySelector("#known-list");
const status = document.querySelector("#status");
const helperStatus = document.querySelector("#helper-status");
const templateList = document.querySelector("#template-list");
const installedPicker = document.querySelector("#installed-picker");
const installedList = document.querySelector("#installed-list");
const templateSearch = document.querySelector("#template-search");

console.log("[Sequence Wheel] main.js loaded");

function guidOf(sequence) {
  return String(sequence.guid);
}

function loadSequenceColors() {
  try {
    return JSON.parse(localStorage.getItem("sequenceWheel.colors") || "{}");
  } catch (_) {
    return {};
  }
}

function colorOf(sequence, index) {
  return sequenceColors[guidOf(sequence)] || DEFAULT_COLORS[index % DEFAULT_COLORS.length];
}

function saveSequenceColor(sequence, color) {
  sequenceColors[guidOf(sequence)] = color;
  localStorage.setItem("sequenceWheel.colors", JSON.stringify(sequenceColors));
  pushHelperState(true);
}

function loadTemplates() {
  try {
    const saved = JSON.parse(localStorage.getItem("sequenceWheel.templates") || "[]");
    return Array.isArray(saved) ? saved : [];
  } catch (_) {
    return [];
  }
}

function saveTemplates() {
  localStorage.setItem("sequenceWheel.templates", JSON.stringify(templates));
  renderTemplates();
  pushHelperState(true);
}

function appendPalette(parent, color, onPick) {
  const picker = document.createElement("button");
  const palette = document.createElement("div");
  picker.type = "button";
  picker.className = "sequence-color-trigger";
  picker.style.backgroundColor = color;
  palette.className = "sequence-palette";
  palette.hidden = true;
  PALETTE_COLORS.forEach(candidate => {
    const swatch = document.createElement("button");
    swatch.type = "button";
    swatch.className = "sequence-color-swatch";
    swatch.style.backgroundColor = candidate;
    swatch.addEventListener("click", () => {
      picker.style.backgroundColor = candidate;
      palette.hidden = true;
      onPick(candidate);
    });
    palette.appendChild(swatch);
  });
  picker.addEventListener("click", () => { palette.hidden = !palette.hidden; });
  parent.appendChild(picker);
  parent.appendChild(palette);
}

async function addTemplates() {
  try {
    const picked = await localFileSystem.getFileForOpening({ types: ["mogrt"], allowMultiple: true });
    const files = Array.isArray(picked) ? picked : picked ? [picked] : [];
    for (const file of files) {
      const path = localFileSystem.getNativePath(file);
      if (templates.some(item => item.path === path)) continue;
      templates.push({
        id: `${Date.now()}-${templates.length}`,
        name: file.name.replace(/\.mogrt$/i, ""),
        path,
        token: await localFileSystem.createPersistentToken(file),
        color: PALETTE_COLORS[(templates.length + 6) % PALETTE_COLORS.length]
      });
    }
    saveTemplates();
  } catch (error) {
    status.textContent = `無法新增模板：${error.message || error}`;
  }
}

function renderTemplates() {
  templateList.innerHTML = "";
  templates.forEach(template => {
    const row = document.createElement("div");
    const name = document.createElement("span");
    const remove = document.createElement("button");
    row.className = "template-row";
    name.textContent = template.name;
    name.title = template.path;
    remove.type = "button";
    remove.className = "template-remove";
    remove.textContent = "移除";
    remove.addEventListener("click", () => {
      templates = templates.filter(item => item.id !== template.id);
      saveTemplates();
    });
    appendPalette(row, template.color, color => {
      template.color = color;
      saveTemplates();
    });
    const palette = row.lastChild;
    row.appendChild(name);
    row.appendChild(remove);
    row.appendChild(palette);
    templateList.appendChild(row);
  });
}

function renderInstalledTemplates() {
  const query = templateSearch.value.trim().toLocaleLowerCase();
  installedList.innerHTML = "";
  installedTemplates
    .filter(template => !query || template.name.toLocaleLowerCase().includes(query))
    .forEach(template => {
      const row = document.createElement("div");
      const name = document.createElement("span");
      const add = document.createElement("button");
      const alreadyAdded = templates.some(item => item.path === template.path);
      row.className = "installed-row";
      name.textContent = template.name;
      name.title = template.path;
      add.type = "button";
      add.textContent = alreadyAdded ? "已加入" : "＋";
      add.disabled = alreadyAdded;
      add.addEventListener("click", () => {
        templates.push({
          id: `${Date.now()}-${templates.length}`,
          name: template.name,
          path: template.path,
          token: null,
          color: PALETTE_COLORS[(templates.length + 6) % PALETTE_COLORS.length]
        });
        saveTemplates();
        renderInstalledTemplates();
      });
      row.appendChild(name);
      row.appendChild(add);
      installedList.appendChild(row);
    });
}

async function refreshInstalledTemplates() {
  try {
    await pushHelperState(true);
    const response = await fetch(`${HELPER_URL}/templates`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    installedTemplates = await response.json();
    renderInstalledTemplates();
  } catch (error) {
    status.textContent = `無法讀取 My Templates：${error.message || error}`;
  }
}

function remember(sequence) {
  if (!sequence) return;
  const guid = guidOf(sequence);
  known = [sequence, ...known.filter(item => guidOf(item) !== guid)].slice(0, MAX_ITEMS);
  activeGuid = guid;
  renderKnown();
}

function renderKnown() {
  knownList.innerHTML = "";
  known.forEach((sequence, index) => {
    const item = document.createElement("div");
    const name = document.createElement("span");
    const picker = document.createElement("button");
    const palette = document.createElement("div");
    name.textContent = sequence.name;
    picker.type = "button";
    picker.className = "sequence-color-trigger";
    picker.style.backgroundColor = colorOf(sequence, index);
    picker.title = `設定 ${sequence.name} 的轉盤顏色`;
    palette.className = "sequence-palette";
    palette.hidden = true;
    PALETTE_COLORS.forEach(color => {
      const swatch = document.createElement("button");
      swatch.type = "button";
      swatch.className = "sequence-color-swatch";
      swatch.style.backgroundColor = color;
      swatch.title = color;
      swatch.addEventListener("click", () => {
        picker.style.backgroundColor = color;
        palette.hidden = true;
        saveSequenceColor(sequence, color);
      });
      palette.appendChild(swatch);
    });
    picker.addEventListener("click", () => { palette.hidden = !palette.hidden; });
    item.appendChild(picker);
    item.appendChild(name);
    item.appendChild(palette);
    item.title = sequence.name;
    if (guidOf(sequence) === activeGuid) item.className = "active";
    knownList.appendChild(item);
  });
}

function setHelperConnected(connected, error) {
  const reason = error ? ` — ${error.message || error}` : "";
  helperStatus.textContent = connected
    ? `v${BUILD_VERSION} 背景 Helper：已連線（全 Premiere 生效）`
    : `v${BUILD_VERSION} 背景 Helper：未連線${reason}`;
  helperStatus.classList.toggle("connected", connected);
}

async function pushHelperState(force) {
  const payload = JSON.stringify({
    activeGuid,
    mogrtRoot,
    sequences: known.map((sequence, index) => ({
      guid: guidOf(sequence),
      name: sequence.name,
      color: colorOf(sequence, index)
    })),
    templates: templates.map(template => ({
      id: template.id,
      name: template.name,
      color: template.color
    }))
  });
  try {
    const response = await fetch(`${HELPER_URL}/state`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: payload
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    setHelperConnected(true);
  } catch (error) {
    console.error("[Sequence Wheel] Helper state failed", error);
    setHelperConnected(false, error);
  }
}

async function insertTemplate(template) {
  try {
    const sequence = project && await project.getActiveSequence();
    if (!sequence) throw new Error("沒有開啟的 Sequence");
    let path = template.path;
    if (template.token) {
      const entry = await localFileSystem.getEntryForPersistentToken(template.token);
      path = localFileSystem.getNativePath(entry);
    }
    const time = await sequence.getPlayerPosition();
    const videoTrackCount = await sequence.getVideoTrackCount();
    const topVideoTrackIndex = Math.max(0, videoTrackCount - 1);
    const editor = premiere.SequenceEditor.getEditor(sequence);
    const inserted = await editor.insertMogrtFromPath(path, time, topVideoTrackIndex, 0);
    if (!inserted || !inserted.length) throw new Error("Premiere 沒有插入模板");
    status.textContent = `已插入模板：${template.name}`;
  } catch (error) {
    status.textContent = `模板插入失敗：${error.message || error}`;
  }
}

async function activateSequence(target) {
  if (!target || !project) return;
  try {
    const switched = await project.setActiveSequence(target);
    if (!switched) await project.openSequence(target);
    remember(target);
    status.textContent = `目前：${target.name}`;
    await pushHelperState(true);
  } catch (error) {
    status.textContent = `切換失敗：${error.message || error}`;
  }
}

async function pollHelperCommand() {
  if (helperRequestRunning) return;
  helperRequestRunning = true;
  try {
    const response = await fetch(`${HELPER_URL}/command`);
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    const command = await response.json();
    setHelperConnected(true);
    if (command.type === "template" && command.id) {
      const template = templates.find(item => item.id === String(command.id));
      if (template) await insertTemplate(template);
    } else if (command.guid) {
      const target = known.find(sequence => guidOf(sequence) === String(command.guid));
      if (target) await activateSequence(target);
    }
  } catch (error) {
    console.error("[Sequence Wheel] Helper command failed", error);
    setHelperConnected(false, error);
  } finally {
    helperRequestRunning = false;
  }
}

async function syncActive() {
  try {
    project = await premiere.Project.getActiveProject();
    const sequence = project && await project.getActiveSequence();
    if (sequence && guidOf(sequence) !== activeGuid) remember(sequence);
    status.textContent = sequence ? `目前：${sequence.name}` : "沒有開啟的 Sequence";
    await pushHelperState(false);
  } catch (error) {
    status.textContent = `無法讀取 Premiere：${error.message || error}`;
  }
}

function showWheel(clientX, clientY) {
  if (!known.length) {
    status.textContent = "尚未識別任何 Sequence";
    return;
  }
  // ponytail: ten recent tabs keeps labels usable; add paging only after real users exceed it.
  wheelOrigin = { x: clientX, y: clientY };
  wheel.style.left = `${clientX}px`;
  wheel.style.top = `${clientY}px`;
  wheelItems.innerHTML = "";

  known.forEach((sequence, index) => {
    const point = geometry.itemPosition(index, known.length, 96);
    const item = document.createElement("div");
    item.className = `wheel-item${guidOf(sequence) === activeGuid ? " current" : ""}`;
    item.textContent = sequence.name;
    item.style.marginLeft = `${point.x}px`;
    item.style.marginTop = `${point.y}px`;
    wheelItems.appendChild(item);
  });

  selectedIndex = -1;
  wheel.hidden = false;
  status.textContent = "移向 Sequence，放開右鍵切換";
}

function updateSelection(clientX, clientY) {
  if (wheel.hidden || !wheelOrigin) return;
  selectedIndex = geometry.itemIndex(
    clientX - wheelOrigin.x,
    clientY - wheelOrigin.y,
    known.length,
    DEAD_ZONE
  );
  [...wheelItems.children].forEach((item, index) => {
    item.classList.toggle("active", index === selectedIndex);
  });
}

async function hideWheel(commit) {
  clearTimeout(holdTimer);
  holdTimer = null;
  if (wheel.hidden) return;
  wheel.hidden = true;

  const target = commit && selectedIndex >= 0 ? known[selectedIndex] : null;
  wheelOrigin = null;
  selectedIndex = -1;
  await activateSequence(target);
}

function isRightButton(event) {
  return event.button === 2 || event.which === 3 || (event.buttons & 2) === 2;
}

function beginRightHold(event) {
  if (!isRightButton(event) || holdingRightButton) return;
  event.preventDefault();
  holdingRightButton = true;
  status.textContent = "已偵測右鍵，請按住…";
  clearTimeout(holdTimer);
  holdTimer = setTimeout(() => showWheel(event.clientX, event.clientY), HOLD_MS);
}

function beginFromContextMenu(event) {
  event.preventDefault();
  if (holdingRightButton) return;
  holdingRightButton = true;
  awaitingFallbackClick = true;
  showWheel(event.clientX, event.clientY);
  status.textContent = "已收到右鍵；移動後放開，若未切換請點一下";
}

function endRightHold(event) {
  if (!holdingRightButton || !isRightButton(event)) return;
  event.preventDefault();
  holdingRightButton = false;
  awaitingFallbackClick = false;
  hideWheel(true);
}

pad.addEventListener("contextmenu", beginFromContextMenu);
pad.addEventListener("mousedown", beginRightHold);
pad.addEventListener("pointerdown", beginRightHold);
window.addEventListener("mousemove", event => updateSelection(event.clientX, event.clientY));
window.addEventListener("pointermove", event => updateSelection(event.clientX, event.clientY));
window.addEventListener("mouseup", endRightHold);
window.addEventListener("pointerup", endRightHold);
window.addEventListener("click", () => {
  if (!awaitingFallbackClick || wheel.hidden) return;
  holdingRightButton = false;
  awaitingFallbackClick = false;
  hideWheel(true);
});
window.addEventListener("blur", () => {
  holdingRightButton = false;
  awaitingFallbackClick = false;
  hideWheel(false);
});
document.querySelector("#refresh").addEventListener("click", syncActive);
document.querySelector("#add-template").addEventListener("click", addTemplates);
document.querySelector("#browse-installed").addEventListener("click", async () => {
  installedPicker.hidden = !installedPicker.hidden;
  if (!installedPicker.hidden) await refreshInstalledTemplates();
});
templateSearch.addEventListener("input", renderInstalledTemplates);

async function initialize() {
  if (premiere) {
    await syncActive();
    if (!pollTimer) pollTimer = setInterval(syncActive, 500);
    if (!helperPollTimer) helperPollTimer = setInterval(pollHelperCommand, 80);
    return;
  }
  try {
    premiere = require("premierepro");
    localFileSystem = require("uxp").storage.localFileSystem;
    renderTemplates();
    mogrtRoot = await premiere.SequenceEditor.getInstalledMogrtPath();
    await syncActive();
    pollTimer = setInterval(syncActive, 500);
    helperPollTimer = setInterval(pollHelperCommand, 80);
  } catch (error) {
    status.textContent = "請在 Premiere UXP Developer Tools 中載入此外掛";
  }
}

// Premiere Pro's HTML starter panel is initialized directly by the document.
// Registering a UXP panel entrypoint here prevents this host from rendering it.
initialize();
