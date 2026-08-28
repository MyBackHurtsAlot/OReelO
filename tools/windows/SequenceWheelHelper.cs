using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SequenceWheelHelper
{
    internal static class Diagnostics
    {
        public static int MouseEvents;
        public static int RightDowns;
        public static int AcceptedRightDowns;
        public static int WheelOpens;
        public static int RightUps;
        public static string LastForeground = "";
    }

    internal sealed class SequenceItem
    {
        public string guid { get; set; }
        public string name { get; set; }
        public string color { get; set; }
    }

    internal sealed class TemplateItem
    {
        public string id { get; set; }
        public string name { get; set; }
        public string color { get; set; }
        public string path { get; set; }
    }

    internal sealed class SequenceState
    {
        public List<SequenceItem> sequences { get; set; }
        public List<TemplateItem> templates { get; set; }
        public string mogrtRoot { get; set; }
        public string activeGuid { get; set; }
    }

    internal sealed class CommandReply
    {
        public string type { get; set; }
        public string guid { get; set; }
        public string id { get; set; }
    }

    internal sealed class LocalBridge : IDisposable
    {
        private readonly object gate = new object();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 17321);
        private volatile bool running;
        private SequenceState state = new SequenceState { sequences = new List<SequenceItem>() };
        private List<TemplateItem> installedTemplates = new List<TemplateItem>();
        private string installedRoot = "";
        private CommandReply pendingCommand;
        private DateTime lastStateAt = DateTime.MinValue;

        public void Start()
        {
            listener.Start();
            running = true;
            Thread thread = new Thread(ListenLoop);
            thread.IsBackground = true;
            thread.Name = "OReelO local bridge";
            thread.Start();
        }

        public List<SequenceItem> SnapshotSequences()
        {
            lock (gate)
            {
                return new List<SequenceItem>(state.sequences ?? new List<SequenceItem>());
            }
        }

        public List<TemplateItem> SnapshotTemplates()
        {
            lock (gate)
            {
                return new List<TemplateItem>(state.templates ?? new List<TemplateItem>());
            }
        }

        public List<TemplateItem> SnapshotInstalledTemplates()
        {
            lock (gate) { return new List<TemplateItem>(installedTemplates); }
        }

        private void RefreshInstalledTemplates(string root)
        {
            if (String.IsNullOrEmpty(root)) return;
            string requested = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string allowed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Adobe", "Common", "Motion Graphics Templates");
            allowed = Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!requested.Equals(allowed, StringComparison.OrdinalIgnoreCase) || requested.Equals(installedRoot, StringComparison.OrdinalIgnoreCase)) return;

            List<TemplateItem> found = new List<TemplateItem>();
            if (Directory.Exists(requested))
            {
                foreach (string file in Directory.GetFiles(requested, "*.mogrt", SearchOption.AllDirectories))
                    found.Add(new TemplateItem { name = Path.GetFileNameWithoutExtension(file), path = file });
                found.Sort(delegate(TemplateItem a, TemplateItem b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.name, b.name); });
            }
            lock (gate)
            {
                installedTemplates = found;
                installedRoot = requested;
            }
        }

        public string ActiveGuid
        {
            get { lock (gate) { return state.activeGuid; } }
        }

        public void QueueCommand(CommandReply command)
        {
            lock (gate) { pendingCommand = command; }
        }

        private void ListenLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { ProcessClient(client); });
                }
                catch (SocketException)
                {
                    if (running) throw;
                }
            }
        }

        private void ProcessClient(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 2000;
                client.SendTimeout = 2000;
                NetworkStream stream = client.GetStream();
                byte[] request = ReadRequest(stream);
                string raw = Encoding.UTF8.GetString(request);
                int split = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (split < 0) { WriteResponse(stream, 400, "{}"); return; }

                string[] firstLine = raw.Substring(0, raw.IndexOf("\r\n", StringComparison.Ordinal)).Split(' ');
                if (firstLine.Length < 2) { WriteResponse(stream, 400, "{}"); return; }
                string method = firstLine[0];
                string path = firstLine[1];
                string body = raw.Substring(split + 4);

                if (method == "OPTIONS")
                {
                    WriteResponse(stream, 204, "");
                }
                else if (method == "GET" && path == "/health")
                {
                    int count;
                    DateTime updated;
                    lock (gate)
                    {
                        count = state.sequences == null ? 0 : state.sequences.Count;
                        updated = lastStateAt;
                    }
                    WriteResponse(stream, 200, json.Serialize(new
                    {
                        ok = true,
                        sequenceCount = count,
                        templateCount = state.templates == null ? 0 : state.templates.Count,
                        installedTemplateCount = installedTemplates.Count,
                        lastStateAt = updated == DateTime.MinValue ? null : updated.ToString("o"),
                        mouseEvents = Diagnostics.MouseEvents,
                        rightDowns = Diagnostics.RightDowns,
                        acceptedRightDowns = Diagnostics.AcceptedRightDowns,
                        wheelOpens = Diagnostics.WheelOpens,
                        rightUps = Diagnostics.RightUps,
                        lastForeground = Diagnostics.LastForeground
                    }));
                }
                else if (method == "POST" && path == "/state")
                {
                    SequenceState incoming = json.Deserialize<SequenceState>(body);
                    if (incoming == null) incoming = new SequenceState();
                    if (incoming.sequences == null) incoming.sequences = new List<SequenceItem>();
                    if (incoming.templates == null) incoming.templates = new List<TemplateItem>();
                    RefreshInstalledTemplates(incoming.mogrtRoot);
                    lock (gate)
                    {
                        state = incoming;
                        lastStateAt = DateTime.UtcNow;
                    }
                    WriteResponse(stream, 200, "{\"ok\":true}");
                }
                else if (method == "GET" && path == "/templates")
                {
                    WriteResponse(stream, 200, json.Serialize(SnapshotInstalledTemplates()));
                }
                else if (method == "GET" && path == "/command")
                {
                    CommandReply command;
                    lock (gate) { command = pendingCommand; pendingCommand = null; }
                    WriteResponse(stream, 200, json.Serialize(command ?? new CommandReply()));
                }
                else
                {
                    WriteResponse(stream, 404, "{}");
                }
            }
        }

        private static byte[] ReadRequest(NetworkStream stream)
        {
            MemoryStream data = new MemoryStream();
            byte[] buffer = new byte[4096];
            int headerEnd = -1;
            int contentLength = 0;

            while (data.Length < 1024 * 1024)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;
                data.Write(buffer, 0, read);
                byte[] bytes = data.ToArray();
                if (headerEnd < 0)
                {
                    headerEnd = FindHeaderEnd(bytes);
                    if (headerEnd >= 0)
                    {
                        string headers = Encoding.ASCII.GetString(bytes, 0, headerEnd);
                        contentLength = ParseContentLength(headers);
                    }
                }
                if (headerEnd >= 0 && bytes.Length >= headerEnd + 4 + contentLength) break;
            }
            return data.ToArray();
        }

        private static int FindHeaderEnd(byte[] bytes)
        {
            for (int i = 0; i <= bytes.Length - 4; i++)
            {
                if (bytes[i] == 13 && bytes[i + 1] == 10 && bytes[i + 2] == 13 && bytes[i + 3] == 10) return i;
            }
            return -1;
        }

        private static int ParseContentLength(string headers)
        {
            string[] lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.None);
            foreach (string line in lines)
            {
                if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) continue;
                int value;
                if (int.TryParse(line.Substring(line.IndexOf(':') + 1).Trim(), out value)) return value;
            }
            return 0;
        }

        private static void WriteResponse(NetworkStream stream, int status, string body)
        {
            byte[] payload = Encoding.UTF8.GetBytes(body);
            string label = status == 200 ? "OK" : status == 204 ? "No Content" : status == 404 ? "Not Found" : "Bad Request";
            string headers = "HTTP/1.1 " + status + " " + label + "\r\n" +
                "Content-Type: application/json; charset=utf-8\r\n" +
                "Content-Length: " + payload.Length + "\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                "Access-Control-Allow-Headers: Content-Type\r\n" +
                "Connection: close\r\n\r\n";
            byte[] prefix = Encoding.ASCII.GetBytes(headers);
            stream.Write(prefix, 0, prefix.Length);
            if (payload.Length > 0) stream.Write(payload, 0, payload.Length);
        }

        public void Dispose()
        {
            running = false;
            listener.Stop();
        }
    }

    internal sealed class WheelForm : Form
    {
        private const int DeadZone = 30;
        private const int TemplateThreshold = 145;
        private List<SequenceItem> sequences = new List<SequenceItem>();
        private List<TemplateItem> templates = new List<TemplateItem>();
        private Point inputOriginScreen;
        private Point displayCenterScreen;
        private double selectionScale = 1.0;
        private int selectedSequenceIndex = -1;
        private int selectedTemplateIndex = -1;
        private string activeGuid;

        public WheelForm()
        {
            ClientSize = new Size(520, 520);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        public void Open(Point origin, Point current, List<SequenceItem> items, List<TemplateItem> templateItems, string active)
        {
            inputOriginScreen = origin;
            sequences = items;
            templates = templateItems;
            activeGuid = active;
            Rectangle area = Screen.FromPoint(origin).WorkingArea;
            displayCenterScreen = ClampCenter(area, origin, Size);
            Location = new Point(displayCenterScreen.X - Width / 2, displayCenterScreen.Y - Height / 2);
            Show();
            NativeMethods.RECT actualBounds;
            if (NativeMethods.GetWindowRect(Handle, out actualBounds) && Width > 0)
                selectionScale = Math.Max(1.0, (actualBounds.right - actualBounds.left) / (double)Width);
            UpdatePointer(current);
            Invalidate();
        }

        public void UpdatePointer(Point current)
        {
            int dx = current.X - inputOriginScreen.X;
            int dy = current.Y - inputOriginScreen.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int templateThreshold = (int)Math.Round(TemplateThreshold * selectionScale);
            int deadZone = (int)Math.Round(DeadZone * selectionScale);
            bool templatesOnly = templates.Count > 0 && sequences.Count == 0;
            selectedTemplateIndex = templatesOnly
                ? ItemIndex(dx, dy, templates.Count, deadZone)
                : templates.Count > 0 && distance >= templateThreshold
                    ? ItemIndex(dx, dy, templates.Count, templateThreshold)
                    : -1;
            selectedSequenceIndex = selectedTemplateIndex < 0
                ? ItemIndex(dx, dy, sequences.Count, deadZone)
                : -1;
            Invalidate();
        }

        internal static Point ClampCenter(Rectangle area, Point requested, Size window)
        {
            int halfWidth = window.Width / 2;
            int halfHeight = window.Height / 2;
            return new Point(
                Math.Max(area.Left + halfWidth, Math.Min(requested.X, area.Right - halfWidth)),
                Math.Max(area.Top + halfHeight, Math.Min(requested.Y, area.Bottom - halfHeight)));
        }

        public CommandReply CloseAndGetSelection(Point current)
        {
            UpdatePointer(current);
            CommandReply selected = null;
            if (selectedTemplateIndex >= 0 && selectedTemplateIndex < templates.Count)
                selected = new CommandReply { type = "template", id = templates[selectedTemplateIndex].id };
            else if (selectedSequenceIndex >= 0 && selectedSequenceIndex < sequences.Count)
                selected = new CommandReply { type = "sequence", guid = sequences[selectedSequenceIndex].guid };
            Hide();
            return selected;
        }

        public void Cancel()
        {
            Hide();
            selectedSequenceIndex = -1;
            selectedTemplateIndex = -1;
        }

        internal static int ItemIndex(int dx, int dy, int count, int deadZone)
        {
            if (count <= 0 || Math.Sqrt(dx * dx + dy * dy) < deadZone) return -1;
            double angle = Math.Atan2(dy, dx) + Math.PI / 2;
            while (angle < 0) angle += Math.PI * 2;
            angle %= Math.PI * 2;
            return (int)Math.Floor((angle + Math.PI / count) / (Math.PI * 2 / count)) % count;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Point center = PointToClient(displayCenterScreen);
            Rectangle outerDisk = new Rectangle(center.X - 215, center.Y - 215, 430, 430);
            Rectangle sequenceDisk = templates.Count > 0
                ? new Rectangle(center.X - 138, center.Y - 138, 276, 276)
                : outerDisk;

            if (templates.Count > 0)
            {
                float templateSweep = 360f / templates.Count;
                for (int i = 0; i < templates.Count; i++)
                {
                    double angle = i * Math.PI * 2 / templates.Count - Math.PI / 2;
                    bool selected = i == selectedTemplateIndex;
                    Color fill = FillColor(templates[i].color, selected, false);
                    float start = -90f - templateSweep / 2f + i * templateSweep;
                    using (Brush brush = new SolidBrush(fill)) g.FillPie(brush, outerDisk, start, templateSweep);
                    using (Pen border = new Pen(Color.FromArgb(210, 190, 195, 205), selected ? 3 : 1)) g.DrawPie(border, outerDisk, start, templateSweep);
                    Rectangle box = new Rectangle(
                        center.X + (int)(Math.Cos(angle) * 178) - 58,
                        center.Y + (int)(Math.Sin(angle) * 178) - 22,
                        116, 44);
                    TextRenderer.DrawText(g, templates[i].name ?? "Template", Font, box, TextColor(fill),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
                }
            }

            float sweep = sequences.Count == 0 ? 360f : 360f / sequences.Count;

            for (int i = 0; i < sequences.Count; i++)
            {
                double angle = i * Math.PI * 2 / sequences.Count - Math.PI / 2;
                bool selected = i == selectedSequenceIndex;
                bool active = sequences[i].guid == activeGuid;
                Color fill = FillColor(sequences[i], selected, active);
                float start = -90f - sweep / 2f + i * sweep;
                using (Brush brush = new SolidBrush(fill)) g.FillPie(brush, sequenceDisk, start, sweep);
                using (Pen border = new Pen(Color.FromArgb(180, 120, 124, 135), selected ? 3 : 1)) g.DrawPie(border, sequenceDisk, start, sweep);

                int radius = templates.Count > 0 ? 91 : 145;
                int x = center.X + (int)(Math.Cos(angle) * radius) - 60;
                int y = center.Y + (int)(Math.Sin(angle) * radius) - 24;
                Rectangle box = new Rectangle(x, y, 120, 48);
                TextRenderer.DrawText(g, sequences[i].name ?? "Sequence", Font, box, TextColor(fill),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);
            }

            using (Pen ring = new Pen(Color.FromArgb(210, 210, 215, 225), 2))
            {
                g.DrawEllipse(ring, outerDisk);
                if (templates.Count > 0) g.DrawEllipse(ring, sequenceDisk);
            }

            Rectangle centerBox = new Rectangle(center.X - 30, center.Y - 30, 60, 60);
            using (Brush brush = new SolidBrush(Color.FromArgb(255, 40, 42, 47))) g.FillEllipse(brush, centerBox);
            TextRenderer.DrawText(g, "放開\n切換", Font, centerBox, Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        internal static Color FillColor(SequenceItem item, bool selected, bool active)
        {
            return FillColor(item.color, selected, active);
        }

        internal static Color FillColor(string colorValue, bool selected, bool active)
        {
            Color color = active ? Color.FromArgb(57, 82, 108) : Color.FromArgb(55, 57, 64);
            try
            {
                if (!String.IsNullOrEmpty(colorValue)) color = ColorTranslator.FromHtml(colorValue);
            }
            catch { }
            if (!selected) return color;
            return Color.FromArgb(255, Math.Min(255, color.R + 45), Math.Min(255, color.G + 45), Math.Min(255, color.B + 45));
        }

        internal static Color TextColor(Color background)
        {
            int brightness = (background.R * 299 + background.G * 587 + background.B * 114) / 1000;
            return brightness >= 150 ? Color.Black : Color.White;
        }
    }

    internal sealed class AppController : ApplicationContext
    {
        private const int HoldMilliseconds = 210;
        private readonly LocalBridge bridge = new LocalBridge();
        private readonly WheelForm wheel = new WheelForm();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly GlobalMouseHook hook;
        private readonly GlobalKeyboardHook keyboardHook;
        private bool tracking;
        private bool wheelOpen;
        private bool suppressCancelledRightUp;
        private bool templatesMode;
        private DateTime pressedAt;
        private Point origin;
        private Point current;

        public AppController()
        {
            bridge.Start();
            hook = new GlobalMouseHook(this);
            keyboardHook = new GlobalKeyboardHook(this);
            timer.Interval = 15;
            timer.Tick += OnTick;
            timer.Start();
        }

        public bool IsTracking { get { return tracking; } }

        public bool CancelWithEscape()
        {
            if (!tracking) return false;
            tracking = false;
            suppressCancelledRightUp = true;
            if (wheelOpen) wheel.Cancel();
            wheelOpen = false;
            return true;
        }

        public bool ConsumeCancelledRightUp()
        {
            if (!suppressCancelledRightUp) return false;
            suppressCancelledRightUp = false;
            return true;
        }

        public bool BeginRightDown(Point point)
        {
            Interlocked.Increment(ref Diagnostics.RightDowns);
            templatesMode = (NativeMethods.GetAsyncKeyState(0x10) & 0x8000) != 0;
            int itemCount = templatesMode ? bridge.SnapshotTemplates().Count : bridge.SnapshotSequences().Count;
            if (tracking || !IsPremiereForeground() || itemCount == 0) return false;
            Interlocked.Increment(ref Diagnostics.AcceptedRightDowns);
            tracking = true;
            wheelOpen = false;
            pressedAt = DateTime.UtcNow;
            origin = point;
            current = point;
            return true;
        }

        public void Move(Point point)
        {
            if (!tracking) return;
            current = point;
            if (wheelOpen) wheel.UpdatePointer(point);
        }

        public void EndRightUp(Point point)
        {
            if (!tracking) return;
            Interlocked.Increment(ref Diagnostics.RightUps);
            current = point;
            bool wasOpen = wheelOpen;
            tracking = false;
            wheelOpen = false;
            if (!wasOpen)
            {
                GlobalMouseHook.ReplayRightClick();
                return;
            }
            CommandReply selected = wheel.CloseAndGetSelection(point);
            if (selected != null) bridge.QueueCommand(selected);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!tracking || wheelOpen) return;
            if ((DateTime.UtcNow - pressedAt).TotalMilliseconds < HoldMilliseconds) return;
            List<SequenceItem> sequences = bridge.SnapshotSequences();
            List<TemplateItem> templates = bridge.SnapshotTemplates();
            if (templatesMode && templates.Count == 0 || !templatesMode && sequences.Count == 0) return;
            wheelOpen = true;
            Interlocked.Increment(ref Diagnostics.WheelOpens);
            wheel.Open(
                origin,
                current,
                templatesMode ? new List<SequenceItem>() : sequences,
                templatesMode ? templates : new List<TemplateItem>(),
                bridge.ActiveGuid);
        }

        private static bool IsPremiereForeground()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            uint processId;
            NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            try
            {
                string name = Process.GetProcessById((int)processId).ProcessName;
                Diagnostics.LastForeground = name;
                return name.IndexOf("Adobe Premiere Pro", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                Diagnostics.LastForeground = "<unavailable>";
                return false;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                keyboardHook.Dispose();
                hook.Dispose();
                wheel.Dispose();
                bridge.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const uint LLMHF_INJECTED = 0x00000001;
        private readonly AppController controller;
        private readonly NativeMethods.LowLevelMouseProc callback;
        private IntPtr handle;

        public GlobalMouseHook(AppController appController)
        {
            controller = appController;
            callback = HookCallback;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
                handle = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, callback, NativeMethods.GetModuleHandle(module.ModuleName), 0);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                Interlocked.Increment(ref Diagnostics.MouseEvents);
                NativeMethods.MSLLHOOKSTRUCT data = (NativeMethods.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.MSLLHOOKSTRUCT));
                if ((data.flags & LLMHF_INJECTED) == 0)
                {
                    Point point = new Point(data.pt.x, data.pt.y);
                    int message = wParam.ToInt32();
                    if (message == WM_RBUTTONDOWN && controller.BeginRightDown(point)) return new IntPtr(1);
                    if (message == WM_MOUSEMOVE && controller.IsTracking) controller.Move(point);
                    if (message == WM_RBUTTONUP && controller.IsTracking)
                    {
                        controller.EndRightUp(point);
                        return new IntPtr(1);
                    }
                    if (message == WM_RBUTTONUP && controller.ConsumeCancelledRightUp()) return new IntPtr(1);
                }
            }
            return NativeMethods.CallNextHookEx(handle, code, wParam, lParam);
        }

        public static void ReplayRightClick()
        {
            NativeMethods.INPUT[] input = new NativeMethods.INPUT[2];
            input[0].type = 0;
            input[0].mi.dwFlags = 0x0008;
            input[1].type = 0;
            input[1].mi.dwFlags = 0x0010;
            NativeMethods.SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(handle);
            handle = IntPtr.Zero;
        }
    }

    internal sealed class GlobalKeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_ESCAPE = 0x1B;
        private readonly AppController controller;
        private readonly NativeMethods.LowLevelKeyboardProc callback;
        private IntPtr handle;

        public GlobalKeyboardHook(AppController appController)
        {
            controller = appController;
            callback = HookCallback;
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
                handle = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, callback, NativeMethods.GetModuleHandle(module.ModuleName), 0);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        }

        private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            int message = wParam.ToInt32();
            if (code >= 0 && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
            {
                NativeMethods.KBDLLHOOKSTRUCT data = (NativeMethods.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(NativeMethods.KBDLLHOOKSTRUCT));
                if (data.vkCode == VK_ESCAPE && controller.CancelWithEscape()) return new IntPtr(1);
            }
            return NativeMethods.CallNextHookEx(handle, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(handle);
            handle = IntPtr.Zero;
        }
    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int x; public int y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int left; public int top; public int right; public int bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public UIntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { public int vkCode; public int scanCode; public int flags; public int time; public UIntPtr extraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public MOUSEINPUT mi; }
        internal delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);
        internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] internal static extern IntPtr GetModuleHandle(string moduleName);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int virtualKey);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                if (WheelForm.ItemIndex(0, -100, 4, 10) != 0) Environment.Exit(1);
                if (WheelForm.ItemIndex(100, 0, 4, 10) != 1) Environment.Exit(1);
                if (WheelForm.ItemIndex(0, 0, 4, 10) != -1) Environment.Exit(1);
                Point clamped = WheelForm.ClampCenter(new Rectangle(0, 0, 1920, 1080), new Point(10, 1000), new Size(520, 520));
                if (clamped.X != 260 || clamped.Y != 820) Environment.Exit(1);
                if (WheelForm.FillColor(new SequenceItem { color = "#123456" }, false, false).ToArgb() != Color.FromArgb(18, 52, 86).ToArgb()) Environment.Exit(1);
                if (WheelForm.TextColor(Color.Yellow) != Color.Black || WheelForm.TextColor(Color.Navy) != Color.White) Environment.Exit(1);
                Console.WriteLine("helper checks passed");
                return;
            }

            if (args.Length > 0 && args[0] == "--bridge-test")
            {
                using (LocalBridge bridge = new LocalBridge())
                {
                    bridge.Start();
                    using (WebClient client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.ContentType] = "application/json";
                        string state = client.UploadString("http://127.0.0.1:17321/state", "POST", "{\"activeGuid\":\"a\",\"sequences\":[{\"guid\":\"a\",\"name\":\"A\"}]}");
                        if (state.IndexOf("true", StringComparison.Ordinal) < 0 || bridge.SnapshotSequences().Count != 1) Environment.Exit(1);
                        bridge.QueueCommand(new CommandReply { type = "sequence", guid = "a" });
                        string command = client.DownloadString("http://127.0.0.1:17321/command");
                        if (command.IndexOf("\"a\"", StringComparison.Ordinal) < 0) Environment.Exit(1);
                    }
                }
                Console.WriteLine("bridge checks passed");
                return;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "SequenceWheelHelper.SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AppController());
            }
        }
    }
}
