// QuoteTodo — 桌面透明待办看板（M1）
// 编译：csc /target:winexe /r:... /out:QuoteTodo.exe QuoteTodo.cs
// 运行：QuoteTodo.exe            （常驻小组件）
//       QuoteTodo.exe --snapshot out.png   （离屏渲染，用于检查排版）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace QuoteTodo
{
    // ---------------------------------------------------------------- model
    public class Todo
    {
        public string Id = "";
        public string Title = "";
        public bool Done;
        public string Due = "";
        public int Order;
    }

    public class Settings
    {
        public int Rows = 4;
        public double Opacity = 0.82;
        public double X = double.MinValue;
        public double Y = double.MinValue;
        public bool Pinned = true;
        public bool Autostart = false;
        public string ApiKeyEnc = "";
        public string DeviceId = "";
        public int Pages = 3;
        public bool AutoPush = true;
        public int PushDelay = 20;
        public List<string> Bindings = new List<string>();
    }

    public class AppData
    {
        public List<Todo> Todos = new List<Todo>();
        public Settings S = new Settings();
    }

    // ---------------------------------------------------------------- store
    public static class Store
    {
        // 设了 QUOTETODO_HOME 就用它（绿色版 / 测试用），否则放 %APPDATA%\Quote0Todo
        public static readonly string Dir = ResolveDir();
        public static readonly string FilePath = Path.Combine(Dir, "todos.json");

        static string ResolveDir()
        {
            var custom = Environment.GetEnvironmentVariable("QUOTETODO_HOME");
            if (!string.IsNullOrEmpty(custom)) return custom;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Quote0Todo");
        }

        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static AppData Load()
        {
            var d = new AppData();
            try
            {
                if (!File.Exists(FilePath)) return Seed();
                var raw = File.ReadAllText(FilePath, Encoding.UTF8);
                var root = Ser.Deserialize<Dictionary<string, object>>(raw);
                if (root == null) return Seed();

                object tv;
                if (root.TryGetValue("todos", out tv))
                {
                    int order = 0;
                    foreach (var item in AsList(tv))
                    {
                        var m = item as Dictionary<string, object>;
                        if (m == null) continue;
                        var t = new Todo
                        {
                            Id = Str(m, "id", Guid.NewGuid().ToString("N")),
                            Title = Str(m, "title", ""),
                            Done = Bool(m, "done", false),
                            Due = Str(m, "due", ""),
                            Order = Int(m, "order", order)
                        };
                        if (t.Title.Length > 0) d.Todos.Add(t);
                        order++;
                    }
                }

                object sv;
                if (root.TryGetValue("settings", out sv) && sv is Dictionary<string, object>)
                {
                    var m = (Dictionary<string, object>)sv;
                    d.S.Rows = Math.Max(1, Math.Min(8, Int(m, "rows", 4)));
                    d.S.Opacity = Math.Max(0.3, Math.Min(1.0, Dbl(m, "opacity", 0.82)));
                    d.S.X = Dbl(m, "x", double.MinValue);
                    d.S.Y = Dbl(m, "y", double.MinValue);
                    d.S.Pinned = Bool(m, "pinned", true);
                    d.S.Autostart = Bool(m, "autostart", false);
                    d.S.ApiKeyEnc = Str(m, "apiKeyEnc", "");
                    d.S.DeviceId = Str(m, "deviceId", "");
                    d.S.Pages = Math.Max(1, Math.Min(6, Int(m, "pages", 3)));
                    d.S.AutoPush = Bool(m, "autoPush", true);
                    d.S.PushDelay = Math.Max(5, Math.Min(600, Int(m, "pushDelay", 20)));
                    d.S.Bindings = new List<string>();
                    object bv;
                    if (m.TryGetValue("bindings", out bv))
                        foreach (var b in AsList(bv)) d.S.Bindings.Add(b == null ? "" : Convert.ToString(b));
                }
                return d;
            }
            catch
            {
                return Seed();
            }
        }

        public static void Save(AppData d)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var root = new Dictionary<string, object>();
                root["version"] = 2;
                var list = new List<Dictionary<string, object>>();
                foreach (var t in d.Todos)
                {
                    list.Add(new Dictionary<string, object> {
                        {"id", t.Id}, {"title", t.Title}, {"done", t.Done},
                        {"due", t.Due}, {"order", t.Order}
                    });
                }
                root["todos"] = list;
                root["settings"] = new Dictionary<string, object> {
                    {"rows", d.S.Rows}, {"opacity", d.S.Opacity},
                    {"x", d.S.X}, {"y", d.S.Y},
                    {"pinned", d.S.Pinned}, {"autostart", d.S.Autostart},
                    {"apiKeyEnc", d.S.ApiKeyEnc}, {"deviceId", d.S.DeviceId},
                    {"pages", d.S.Pages}, {"autoPush", d.S.AutoPush}, {"pushDelay", d.S.PushDelay},
                    {"bindings", new List<object>(d.S.Bindings.ToArray())}
                };
                File.WriteAllText(FilePath, Ser.Serialize(root), new UTF8Encoding(false));
            }
            catch { }
        }

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(bytes);
            }
            catch { return ""; }
        }

        public static string Unprotect(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return ""; }
        }

        static AppData Seed()
        {
            var d = new AppData();
            string[] titles = {
                "把桌面小组件跑起来",
                "拖到顺手的位置，试试右键菜单",
                "点一下左边的方块，勾掉一条",
                "在网页版里导出 JSON，右键导入"
            };
            for (int i = 0; i < titles.Length; i++)
                d.Todos.Add(new Todo { Id = Guid.NewGuid().ToString("N"), Title = titles[i], Order = i });
            return d;
        }

        static string Str(Dictionary<string, object> m, string k, string def)
        { object v; return m.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : def; }

        // JavaScriptSerializer 的数组可能是 object[] 也可能是 ArrayList，统一摊平
        public static IEnumerable<object> AsList(object o)
        {
            if (o == null || o is string) yield break;
            var seq = o as System.Collections.IEnumerable;
            if (seq == null) yield break;
            foreach (var x in seq) yield return x;
        }

        static bool Bool(Dictionary<string, object> m, string k, bool def)
        { object v; return m.TryGetValue(k, out v) && v != null ? Convert.ToBoolean(v) : def; }

        static int Int(Dictionary<string, object> m, string k, int def)
        { object v; return m.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v) : def; }

        static double Dbl(Dictionary<string, object> m, string k, double def)
        { object v; return m.TryGetValue(k, out v) && v != null ? Convert.ToDouble(v) : def; }
    }

    // ---------------------------------------------------------------- theme
    public static class T
    {
        public static readonly Color Ink = Color.FromRgb(0x17, 0x17, 0x0F);
        public static readonly Color Muted = Color.FromRgb(0x6F, 0x6B, 0x60);
        public static readonly Color Faint = Color.FromRgb(0x9A, 0x96, 0x8C);
        public static readonly Color Line = Color.FromRgb(0xC9, 0xC3, 0xB2);

        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static SolidColorBrush BA(byte a, Color c) { var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)); b.Freeze(); return b; }

        public static readonly FontFamily Cjk = new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");
        public static readonly FontFamily Mono = new FontFamily("Consolas, Segoe UI");
    }

    // ---------------------------------------------------------------- widget
    public class Widget
    {
        public const int CardWidth = 320;
        public const int Margin_ = 14;   // 给投影留的透明边，避免被窗口裁掉

        Window _win;
        Border _card;
        TextBlock _date, _clock, _pageText, _countText, _empty;
        Grid _progress;
        Border _progressFill;
        StackPanel _rows;

        AppData _data;
        int _page;
        DispatcherTimer _tick, _saveTimer, _pushTimer;
        bool _pushing;
        WinForms.NotifyIcon _tray;
        IntPtr _hwnd;
        bool _hidden;
        bool _quitting;

        public void Start()
        {
            _data = Store.Load();

            _card = BuildCard();
            _win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Content = _card,
                Title = "Quote/0 待办"
            };
            _win.MouseLeftButtonDown += Window_MouseDown;

            Place();
            HookTray();
            _win.SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(_win).Handle; RegisterHotkey(); };
            _win.Show();
            _win.Closing += (s, e) =>
            {
                // 托盘图标可能被 Windows 收进溢出区，所以关窗口就是退出，不玩"藏起来"
                if (_quitting) return;
                _quitting = true;
                Save();
                e.Cancel = true;
                _win.Dispatcher.BeginInvoke(new Action(() => Application.Current.Shutdown()));
            };

            _tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _tick.Tick += (s, e) => UpdateHeader();
            _tick.Start();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Store.Save(_data); };
            _win.LocationChanged += (s, e) => { _saveTimer.Stop(); _saveTimer.Start(); };

            Refresh();

            if (Environment.GetEnvironmentVariable("QUOTETODO_SMOKE") == "1")
            {
                _win.Opacity = 0;
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    try
                    {
                        Directory.CreateDirectory(Store.Dir);
                        File.WriteAllText(Path.Combine(Store.Dir, "smoke.log"),
                            "smoke OK " + DateTime.Now.ToString("s"), Encoding.UTF8);
                    }
                    catch { }
                    Quit();
                };
                t.Start();
            }
        }

        public Border BuildForSnapshot(AppData data)
        {
            _data = data;
            var card = BuildCard();
            Populate();
            return card;
        }

        void Place()
        {
            var wa = SystemParameters.WorkArea;
            double w = CardWidth + Margin_ * 2;
            if (_data.S.X == double.MinValue || _data.S.Y == double.MinValue)
            {
                _win.Left = wa.Right - w - 24;
                _win.Top = wa.Top + 24;
            }
            else
            {
                _win.Left = _data.S.X;
                _win.Top = _data.S.Y;
            }
            // 越界的坐标拉回主屏
            if (_win.Left < wa.Left - 50 || _win.Left > wa.Right - 60) _win.Left = wa.Right - w - 24;
            if (_win.Top < wa.Top - 10 || _win.Top > wa.Bottom - 60) _win.Top = wa.Top + 24;
        }

        // ---------------------------------------------------------- UI
        Border BuildCard()
        {
            var bg = T.BA((byte)Math.Round(255 * _data.S.Opacity), Colors.White);
            var card = new Border
            {
                Width = CardWidth,
                Margin = new Thickness(Margin_),   // Window.Padding 对 Window 不生效，留白靠 Margin
                CornerRadius = new CornerRadius(14),
                Background = bg,
                BorderBrush = T.BA(30, Colors.Black),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 11, 14, 10),
                Effect = new DropShadowEffect
                {
                    BlurRadius = 22,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = 0.16,
                    Color = Colors.Black
                }
            };
            TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(card, TextRenderingMode.Grayscale);

            var root = new StackPanel();
            card.Child = root;

            // header
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _date = new TextBlock
            {
                FontFamily = T.Cjk, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.Ink), VerticalAlignment = VerticalAlignment.Center
            };
            _clock = new TextBlock
            {
                FontFamily = T.Mono, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.Ink), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_clock, 1);
            head.Children.Add(_date);
            head.Children.Add(_clock);
            root.Children.Add(head);

            // progress
            _progress = new Grid { Height = 3, Margin = new Thickness(0, 7, 0, 0) };
            _progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0, GridUnitType.Star) });
            _progress.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var track = new Border { Background = T.BA(28, Colors.Black), CornerRadius = new CornerRadius(1.5) };
            Grid.SetColumnSpan(track, 2);
            _progressFill = new Border { Background = T.B(T.Ink), CornerRadius = new CornerRadius(1.5) };
            Grid.SetColumn(_progressFill, 0);
            _progress.Children.Add(track);
            _progress.Children.Add(_progressFill);
            root.Children.Add(_progress);

            // rows
            _rows = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
            root.Children.Add(_rows);

            _empty = new TextBlock
            {
                Text = "暂无待办 · 右键新增",
                FontFamily = T.Cjk, FontSize = 13, Foreground = T.B(T.Muted),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 18),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_empty);

            // footer
            var foot = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _pageText = new TextBlock { FontFamily = T.Cjk, FontSize = 11, Foreground = T.B(T.Muted) };
            _countText = new TextBlock
            {
                FontFamily = T.Cjk, FontSize = 11, Foreground = T.B(T.Muted),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(_countText, 1);
            foot.Children.Add(_pageText);
            foot.Children.Add(_countText);
            root.Children.Add(foot);

            card.MouseLeftButtonDown += Card_MouseDown;
            card.MouseWheel += Card_Wheel;
            card.MouseRightButtonUp += (s, e) =>
            {
                if (IsRow(e.OriginalSource as DependencyObject)) return;
                var cm = BuildMenu();
                cm.PlacementTarget = card;
                cm.IsOpen = true;
                e.Handled = true;
            };
            return card;
        }

        void UpdateHeader()
        {
            var now = DateTime.Now;
            _date.Text = now.ToString("M月d日 ", CultureInfo.InvariantCulture) + Weekday(now);
            _clock.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        static string Weekday(DateTime d)
        {
            string[] w = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            return w[(int)d.DayOfWeek];
        }

        // ---------------------------------------------------------- refresh
        public void Refresh()
        {
            Populate();
            if (_win != null) _win.SizeToContent = SizeToContent.WidthAndHeight;
        }

        class PageModel { public int Index; public List<Todo> Items = new List<Todo>(); public string Mode = "list"; }

        List<PageModel> BuildPages(out int total, out int doneCount, out int undone)
        {
            var open = _data.Todos.Where(t => !t.Done).OrderBy(t => t.Order).ToList();
            var done = _data.Todos.Where(t => t.Done).OrderBy(t => t.Order).ToList();
            var ordered = open.Concat(done).ToList();

            total = _data.Todos.Count;
            doneCount = done.Count;
            undone = total - doneCount;

            int per = Math.Max(1, _data.S.Rows);
            int pages = Math.Max(1, (int)Math.Ceiling(ordered.Count / (double)per));
            var list = new List<PageModel>();
            for (int i = 0; i < pages; i++)
            {
                var pm = new PageModel { Index = i };
                pm.Items = ordered.Skip(i * per).Take(per).ToList();
                pm.Mode = pm.Items.Count == 0 ? (total == 0 ? "empty" : "summary") : "list";
                list.Add(pm);
            }
            return list;
        }

        void Populate()
        {
            UpdateHeader();

            int total, doneCount, undone;
            var pages = BuildPages(out total, out doneCount, out undone);

            if (_page >= pages.Count) _page = pages.Count - 1;
            if (_page < 0) _page = 0;

            var done = _data.Todos.Count(t => t.Done);
            var page = pages[_page];

            _rows.Children.Clear();
            foreach (var t in page.Items) _rows.Children.Add(BuildRow(t));

            _empty.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;

            _pageText.Text = (_page + 1) + " / " + pages.Count;
            _countText.Text = "完成 " + doneCount + " / " + total;

            double pct = total == 0 ? 0 : doneCount * 100.0 / total;
            _progress.ColumnDefinitions[0].Width = new GridLength(pct, GridUnitType.Star);
            _progress.ColumnDefinitions[1].Width = new GridLength(100 - pct, GridUnitType.Star);
        }

        Border BuildRow(Todo t)
        {
            var row = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Padding = new Thickness(3, 0, 3, 0),
                Tag = t
            };

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new Border
            {
                Width = 15, Height = 15, CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1.2),
                BorderBrush = T.B(t.Done ? T.Ink : T.Line),
                Background = t.Done ? T.B(T.Ink) : Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // 15px 的方块太小，外面套一层 30×28 的透明热区
            var hit = new Border
            {
                Width = 30, Height = 28,
                Background = Brushes.Transparent,
                Tag = "chk",
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = box
            };
            hit.MouseLeftButtonDown += (s, e) => { e.Handled = true; ToggleDone(t); };
            g.Children.Add(hit);

            var title = new TextBlock
            {
                Text = string.IsNullOrEmpty(t.Title) ? "（未命名）" : t.Title,
                FontFamily = T.Cjk, FontSize = 15,
                Foreground = T.B(t.Done ? T.Faint : T.Ink),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };
            if (t.Done) title.TextDecorations = TextDecorations.Strikethrough;
            Grid.SetColumn(title, 1);
            g.Children.Add(title);

            bool bold, overdue; string label = DueLabel(t.Due, out bold, out overdue);
            if (label != null)
            {
                var meta = new TextBlock
                {
                    Text = label, FontFamily = T.Cjk, FontSize = 12,
                    FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = T.B(overdue || bold ? T.Ink : T.Muted),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(meta, 2);
                g.Children.Add(meta);
            }

            row.Child = g;
            row.MouseEnter += (s, e) => { if (!_hidden) row.Background = T.BA(10, Colors.Black); };
            row.MouseLeave += (s, e) => { row.Background = Brushes.Transparent; };

            var cm = new ContextMenu();
            var del = new MenuItem { Header = "删除这条" };
            del.Click += (s, e) =>
            {
                _data.Todos.Remove(t);
                Changed();
            };
            cm.Items.Add(del);
            row.ContextMenu = cm;

            return row;
        }

        static string DueLabel(string due, out bool bold, out bool overdue)
        {
            bold = false; overdue = false;
            DateTime d;
            if (string.IsNullOrEmpty(due) || !DateTime.TryParseExact(due, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return null;

            int diff = (int)(d.Date - DateTime.Today).TotalDays;
            if (diff < 0) { bold = true; overdue = true; return "逾期"; }
            if (diff == 0) { bold = true; return "今天"; }
            if (diff == 1) { bold = true; return "明天"; }
            return d.ToString("M/d", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------- actions
        void ToggleDone(Todo t)
        {
            t.Done = !t.Done;
            Save();
            Refresh();
        }

        void Save()
        {
            _data.S.X = _win.Left;
            _data.S.Y = _win.Top;
            Store.Save(_data);
        }

        // 任何数据变动：存盘 + 重绘 + 排队推设备
        void Changed()
        {
            Save();
            Refresh();
            SchedulePush();
        }

        void SchedulePush()
        {
            if (!_data.S.AutoPush) return;
            if (string.IsNullOrEmpty(_data.S.ApiKeyEnc) || string.IsNullOrEmpty(_data.S.DeviceId)) return;
            if (_pushTimer == null)
            {
                _pushTimer = new DispatcherTimer();
                _pushTimer.Tick += (s, e) => { _pushTimer.Stop(); PushAsync(); };
            }
            _pushTimer.Stop();
            _pushTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, _data.S.PushDelay));
            _pushTimer.Start();
        }

        public List<string> PushToDevice()
        {
            var key = Store.Unprotect(_data.S.ApiKeyEnc);
            var dev = _data.S.DeviceId;
            var msgs = new List<string>();
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(dev))
            {
                msgs.Add("还没配置密钥或设备号（右键 → 推送设置）");
                return msgs;
            }

            int total, doneCount, undone;
            var pages = BuildPages(out total, out doneCount, out undone);
            int count = Math.Min(pages.Count, Math.Max(1, _data.S.Pages));
            var now = DateTime.Now;
            var ser = new JavaScriptSerializer();

            for (int i = 0; i < count; i++)
            {
                string bind = i < _data.S.Bindings.Count ? _data.S.Bindings[i] : "";
                if (i > 0 && string.IsNullOrEmpty(bind))
                {
                    msgs.Add("第" + (i + 1) + "页未绑定");
                    continue;
                }

                // showClock = false：画板拿不到设备时钟，右上角改为显示「完成数/总数」
                var wd = Board.BuildWindowData(i, count, _data.S.Rows, pages[i].Items,
                    total, doneCount, undone, pages[i].Mode, false, now);

                var payload = new Dictionary<string, object>
                {
                    {"taskType", "loop"},
                    {"refreshNow", true},
                    {"taskAlias", "待办看板 P" + (i + 1)},
                    {"data", new Dictionary<string, object>
                        {
                            {"page", i + 1}, {"pages", count}, {"total", total},
                            {"done", doneCount}, {"undone", undone},
                            {"generatedAt", now.ToString("o")},
                            {"items", pages[i].Items.Select(t => new Dictionary<string, object>
                                {
                                    {"title", t.Title}, {"done", t.Done}, {"due", t.Due}
                                }).ToList()}
                        }
                    },
                    {"windowData", wd},
                    {"layoutFull", Board.LayoutFull},
                    {"border", 0}
                };
                if (!string.IsNullOrEmpty(bind)) payload["taskKey"] = bind;

                var r = Api.Send(key, dev, "/canvas", "POST", ser.Serialize(payload));
                msgs.Add(r.Ok ? ("第" + (i + 1) + "页 ✓") : ("第" + (i + 1) + "页 ✗ " + Api.Explain(r)));
                if (i < count - 1) Thread.Sleep(180);
            }
            return msgs;
        }

        void PushAsync()
        {
            if (_pushing) return;
            _pushing = true;
            Task.Run(() =>
            {
                var msgs = PushToDevice();
                _win.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _pushing = false;
                    Balloon(string.Join("；", msgs.ToArray()));
                }));
            });
        }

        void Card_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (IsCheckBox(e.OriginalSource as DependencyObject)) return;
            try { _win.DragMove(); } catch { }
        }

        void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { _win.DragMove(); } catch { }
        }

        static bool IsCheckBox(DependencyObject d)
        {
            while (d != null)
            {
                var fe = d as FrameworkElement;
                if (fe != null && "chk".Equals(fe.Tag)) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        static bool IsRow(DependencyObject d)
        {
            while (d != null)
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.Tag is Todo) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        void Card_Wheel(object sender, MouseWheelEventArgs e)
        {
            int per = Math.Max(1, _data.S.Rows);
            int count = _data.Todos.Count;
            int pages = Math.Max(1, (int)Math.Ceiling(count / (double)per));
            if (pages <= 1) return;
            _page += e.Delta < 0 ? 1 : -1;
            if (_page < 0) _page = 0;
            if (_page >= pages) _page = pages - 1;
            Refresh();
        }

        // ---------------------------------------------------------- menu
        ContextMenu BuildMenu()
        {
            var cm = new ContextMenu();

            var add = new MenuItem { Header = "新增待办…" };
            add.Click += (s, e) => AddTodo();
            cm.Items.Add(add);

            var imp = new MenuItem { Header = "从网页版导入…" };
            imp.Click += (s, e) => Import();
            cm.Items.Add(imp);

            cm.Items.Add(new Separator());

            var pushCfg = new MenuItem { Header = "推送设置…" };
            pushCfg.Click += (s, e) => OpenSettings();
            cm.Items.Add(pushCfg);

            var pushNow = new MenuItem { Header = "立即推送到 Quote/0" };
            pushNow.Click += (s, e) => { if (!_pushing) PushAsync(); else Balloon("正在推送…"); };
            cm.Items.Add(pushNow);

            cm.Items.Add(new Separator());

            var rowsMenu = new MenuItem { Header = "每页条数" };
            foreach (int n in new[] { 3, 4, 5, 6 })
            {
                var mi = new MenuItem { Header = n + " 条", IsCheckable = true, IsChecked = _data.S.Rows == n };
                int v = n;
                mi.Click += (s, e) => { _data.S.Rows = v; _page = 0; Changed(); };
                rowsMenu.Items.Add(mi);
            }
            cm.Items.Add(rowsMenu);

            var opMenu = new MenuItem { Header = "透明度" };
            foreach (var pair in new[] { new[] { "70%", "0.70" }, new[] { "82%", "0.82" }, new[] { "92%", "0.92" }, new[] { "不透明", "1" } })
            {
                double v = double.Parse(pair[1], CultureInfo.InvariantCulture);
                var mi = new MenuItem { Header = pair[0], IsCheckable = true, IsChecked = Math.Abs(_data.S.Opacity - v) < 0.01 };
                mi.Click += (s, e) => { _data.S.Opacity = v; _card.Background = T.BA((byte)Math.Round(255 * v), Colors.White); Save(); };
                opMenu.Items.Add(mi);
            }
            cm.Items.Add(opMenu);

            cm.Items.Add(new Separator());

            var through = new MenuItem { Header = "点击穿透 (Ctrl+Alt+L)", IsCheckable = true };
            through.Click += (s, e) => ToggleClickThrough();
            cm.Items.Add(through);

            var auto = new MenuItem { Header = "开机自启", IsCheckable = true, IsChecked = AutostartOn() };
            auto.Click += (s, e) => SetAutostart(!AutostartOn());
            cm.Items.Add(auto);

            cm.Items.Add(new Separator());

            var data = new MenuItem { Header = "打开数据文件夹" };
            data.Click += (s, e) => { Directory.CreateDirectory(Store.Dir); System.Diagnostics.Process.Start("explorer.exe", Store.Dir); };
            cm.Items.Add(data);

            var quit = new MenuItem { Header = "退出" };
            quit.Click += (s, e) => Quit();
            cm.Items.Add(quit);

            return cm;
        }

        // ---------------------------------------------------------- add
        void OpenSettings()
        {
            var dlg = new SettingsDialog(_data);
            dlg.Owner = _win;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var ok = dlg.ShowDialog();
            if (ok == true)
            {
                Store.Save(_data);
                SchedulePush();
                Balloon("设置已保存");
            }
        }

        void AddTodo()
        {
            var text = Prompt.Ask(_win, "新增待办", "今天要做什么？");
            if (text == null) return;
            text = text.Trim();
            if (text.Length == 0) return;
            int maxOrder = _data.Todos.Count == 0 ? 0 : _data.Todos.Max(t => t.Order) + 1;
            _data.Todos.Add(new Todo { Id = Guid.NewGuid().ToString("N"), Title = text, Order = maxOrder });
            Changed();
        }

        void Import()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择网页版导出的 JSON",
                Filter = "JSON|*.json|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var text = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                var ser = new JavaScriptSerializer();
                object parsed = ser.DeserializeObject(text);
                object listObj = parsed;

                var dict = parsed as Dictionary<string, object>;
                if (dict != null)
                {
                    object tv;
                    if (dict.TryGetValue("todos", out tv)) listObj = tv;
                }

                var list = new List<Todo>();
                int order = 0;
                foreach (var item in Store.AsList(listObj))
                {
                    var m = item as Dictionary<string, object>;
                    if (m == null) continue;
                    string title = m.ContainsKey("title") ? Convert.ToString(m["title"]) : "";
                    if (string.IsNullOrEmpty(title)) continue;
                    list.Add(new Todo
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Title = title,
                        Done = m.ContainsKey("done") && Convert.ToBoolean(m["done"]),
                        Due = m.ContainsKey("due") ? Convert.ToString(m["due"]) : "",
                        Order = order++
                    });
                }
                if (list.Count == 0) throw new Exception("empty");

                _data.Todos = list;
                _page = 0;
                Changed();
                Balloon("已导入 " + list.Count + " 条待办");
            }
            catch
            {
                Balloon("导入失败：没读到 todos 数组");
            }
        }

        // ---------------------------------------------------------- tray
        void HookTray()
        {
            _tray = new WinForms.NotifyIcon();
            _tray.Icon = MakeIcon();
            _tray.Text = "Quote/0 待办";
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ShowWidget();

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏", null, (s, e) => { if (_hidden) ShowWidget(); else HideToTray(); });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            var miThrough = new WinForms.ToolStripMenuItem("点击穿透");
            miThrough.Click += (s, e) => ToggleClickThrough();
            menu.Items.Add(miThrough);
            menu.Items.Add("新增待办", null, (s, e) => { ShowWidget(); AddTodo(); });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => Quit());
            _tray.ContextMenuStrip = menu;
        }

        static System.Drawing.Icon MakeIcon()
        {
            var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                using (var b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(23, 23, 15)))
                    g.FillEllipse(b, 2, 2, 28, 28);
            }
            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        void HideToTray()
        {
            _hidden = true;
            _win.Hide();
        }

        void ShowWidget()
        {
            _hidden = false;
            _win.Show();
            _win.Topmost = true;
            Refresh();
        }

        void Balloon(string text)
        {
            if (_tray == null) return;
            _tray.ShowBalloonTip(2500, "Quote/0 待办", text, WinForms.ToolTipIcon.Info);
        }

        void Quit()
        {
            _quitting = true;
            _data.S.X = _win.Left;
            _data.S.Y = _win.Top;
            Store.Save(_data);
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            Application.Current.Shutdown();
        }

        // ---------------------------------------------------------- click-through
        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x20;
        const int WS_EX_LAYERED = 0x80000;
        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2;
        const uint VK_L = 0x4C;

        bool _through;

        void RegisterHotkey()
        {
            if (_hwnd == IntPtr.Zero) return;
            RegisterHotKey(_hwnd, 1, MOD_CONTROL | MOD_ALT, VK_L);
            var src = HwndSource.FromHwnd(_hwnd);
            if (src != null) src.AddHook(HwndHook);
        }

        IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == 1) { ToggleClickThrough(); handled = true; }
            return IntPtr.Zero;
        }

        void ToggleClickThrough()
        {
            _through = !_through;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            ex = _through ? (ex | WS_EX_TRANSPARENT | WS_EX_LAYERED) : (ex & ~WS_EX_TRANSPARENT);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex);
            Balloon(_through ? "已开启点击穿透，按 Ctrl+Alt+L 恢复" : "已恢复点击");
        }

        // ---------------------------------------------------------- autostart
        static string StartupLink()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "QuoteTodo.lnk");
        }

        static bool AutostartOn() { return File.Exists(StartupLink()); }

        static void SetAutostart(bool on)
        {
            try
            {
                string link = StartupLink();
                if (!on)
                {
                    if (File.Exists(link)) File.Delete(link);
                    return;
                }
                string exe = Assembly.GetExecutingAssembly().Location;
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(link);
                sc.TargetPath = exe;
                sc.WorkingDirectory = Path.GetDirectoryName(exe);
                sc.Description = "Quote/0 桌面待办";
                sc.Save();
            }
            catch { }
        }
    }

    // ---------------------------------------------------------------- prompt
    public class Prompt : Window
    {
        string _value;

        public static string Ask(Window owner, string title, string hint)
        {
            var p = new Prompt();
            p.Init(owner, title, hint);
            return p.ShowDialog() == true ? p._value : null;
        }

        void Init(Window owner, string title, string hint)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = owner;
            Topmost = true;
            ShowInTaskbar = false;

            var card = new Border
            {
                Width = 300,
                CornerRadius = new CornerRadius(12),
                Background = T.B(Colors.White),
                BorderBrush = T.BA(30, Colors.Black),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14),
                Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.22, Color = Colors.Black }
            };
            var sp = new StackPanel();
            card.Child = sp;

            sp.Children.Add(new TextBlock
            {
                Text = title, FontFamily = T.Cjk, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.Ink), Margin = new Thickness(0, 0, 0, 10)
            });

            var tb = new TextBox
            {
                FontFamily = T.Cjk, FontSize = 15, BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = T.BA(40, Colors.Black), Background = Brushes.Transparent,
                Padding = new Thickness(0, 4, 0, 6), CaretBrush = T.B(T.Ink), Foreground = T.B(T.Ink)
            };
            tb.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { _value = tb.Text; DialogResult = true; }
                if (e.Key == Key.Escape) { DialogResult = false; }
            };
            sp.Children.Add(tb);

            sp.Children.Add(new TextBlock
            {
                Text = hint + " · 回车保存，Esc 取消",
                FontFamily = T.Cjk, FontSize = 11, Foreground = T.B(T.Muted),
                Margin = new Thickness(0, 9, 0, 0)
            });

            Content = card;
            Loaded += (s, e) => { Activate(); tb.Focus(); };
        }
    }

    // ---------------------------------------------------------------- snapshot
    public static class Snapshot
    {
        public static void Save(string path)
        {
            var data = new AppData();
            data.S.Rows = 4;
            data.Todos = new List<Todo> {
                new Todo{ Title="把原型发给老王", Order=0, Due="2026-10-26" },
                new Todo{ Title="改看板字号，16px 看着舒服", Order=1, Due="2026-10-28" },
                new Todo{ Title="接入桌面小组件", Order=2, Done=true },
                new Todo{ Title="确认透明度够不够", Order=3, Due="2026-10-29" }
            };

            var w = new Widget();
            var card = w.BuildForSnapshot(data);

            int ww = Widget.CardWidth + 180;
            var root = new Grid { Width = ww, Height = 300 };
            var grad = new LinearGradientBrush(
                Color.FromRgb(0xED, 0xEA, 0xE3), Color.FromRgb(0xD7, 0xD2, 0xC7), 40);
            root.Background = grad;

            // 一块"后面的窗口"，用来判断透明度
            var behind = new Border
            {
                Width = 220, Height = 140,
                Background = T.B(Colors.White),
                CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(150, 120, 0, 0),
                Opacity = 0.9
            };
            root.Children.Add(behind);

            card.HorizontalAlignment = HorizontalAlignment.Left;
            card.VerticalAlignment = VerticalAlignment.Top;
            card.Margin = new Thickness(24, 24, 0, 0);
            root.Children.Add(card);

            root.Measure(new Size(ww, 300));
            root.Arrange(new Rect(0, 0, ww, 300));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(ww, 300, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }

    // ---------------------------------------------------------------- 画板排版
    // 与网页版 buildWindowData 一一对应：同一套像素、字号、颜色
    public static class Board
    {
        const int PAD_X = 11, PAD_Y = 8;
        static readonly string[] Week = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

        public static readonly Dictionary<string, object> LayoutFull = new Dictionary<string, object> {
            {"tw", "p-[0px]"},
            {"style", new Dictionary<string, object> { {"padding", "0px"} }}
        };

        class L { public int rowH, gap, title, meta, box; }
        static readonly Dictionary<int, L> Layouts = new Dictionary<int, L> {
            { 2, new L { rowH = 43, gap = 4, title = 18, meta = 14, box = 14 } },
            { 3, new L { rowH = 28, gap = 3, title = 16, meta = 13, box = 12 } },
            { 4, new L { rowH = 21, gap = 2, title = 14, meta = 12, box = 11 } }
        };

        static Dictionary<string, object> S(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        static Dictionary<string, object> N(string type, string tw, Dictionary<string, object> style, object children)
        {
            var props = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(tw)) props["tw"] = tw;
            props["style"] = style ?? new Dictionary<string, object>();
            props["children"] = children ?? "";
            return new Dictionary<string, object> { { "type", type }, { "props", props } };
        }

        static string Font(int px) { return "text-[" + px + "px]-chillduansans"; }

        static string DueText(string due, DateTime now, out bool relative, out bool overdue)
        {
            relative = false; overdue = false;
            DateTime d;
            if (string.IsNullOrEmpty(due) || !DateTime.TryParseExact(due, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return null;
            int diff = (int)(d.Date - now.Date).TotalDays;
            if (diff < 0) { relative = true; overdue = true; return "逾期"; }
            if (diff == 0) { relative = true; return "今天"; }
            if (diff == 1) { relative = true; return "明天"; }
            return d.Month + "/" + d.Day;
        }

        static Dictionary<string, object> Row(Todo t, DateTime now, L L)
        {
            bool relative, overdue;
            string meta = DueText(t.Due, now, out relative, out overdue);
            string lh = Math.Min(L.rowH - 3, L.title + 6) + "px";

            var kids = new List<object>();
            kids.Add(N("div", "", S(
                "width", L.box + "px", "height", L.box + "px", "flexShrink", "0",
                "border", "1px solid #000", "borderRadius", "2px",
                "backgroundColor", t.Done ? "#000000" : "#ffffff"), ""));

            kids.Add(N("span", Font(L.title), S(
                "flexGrow", "1", "flexShrink", "1", "flexBasis", "0%", "minWidth", "0",
                "lineHeight", lh, "whiteSpace", "nowrap", "overflow", "hidden", "textOverflow", "ellipsis",
                "textDecoration", t.Done ? "line-through" : "none",
                "color", t.Done ? "#8A8A8A" : "#000000"),
                string.IsNullOrEmpty(t.Title) ? "（未命名）" : t.Title));

            if (meta != null)
                kids.Add(N("span", Font(L.meta), S(
                    "flexShrink", "0", "lineHeight", lh,
                    "fontWeight", relative ? "700" : "400",
                    "color", "#000000"), meta));

            return N("div", "flex flex-row items-center shrink-0", S("height", L.rowH + "px", "gap", "6px"), kids);
        }

        public static Dictionary<string, object> BuildWindowData(
            int index, int pages, int per, List<Todo> items,
            int total, int doneCount, int undone, string mode, bool showClock, DateTime now)
        {
            L L = Layouts.ContainsKey(per) ? Layouts[per] : Layouts[3];
            string dateLabel = (now.Month) + "月" + now.Day + "日 " + Week[(int)now.DayOfWeek];
            int pct = total > 0 ? (int)Math.Round(doneCount * 100.0 / total) : 0;
            string clock = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            string countText = doneCount + "/" + total;

            var header = N("div", "flex flex-row items-center justify-between shrink-0", S("height", "18px"), new List<object> {
                N("span", Font(13), S("fontWeight", "700", "lineHeight", "18px"), dateLabel),
                N("span", Font(13), S("fontWeight", "700", "lineHeight", "18px"), showClock ? clock : countText)
            });

            var bar = N("div", "", S(
                "marginTop", "4px", "height", "3px", "width", "100%", "flexShrink", "0",
                "backgroundColor", "#E3E0D6"),
                N("div", "", S("height", "3px", "width", pct + "%", "backgroundColor", "#000000"), ""));

            object body;
            if (mode == "summary")
            {
                body = N("div", "flex flex-col items-center justify-center flex-1 min-h-0", S("gap", "2px"), new List<object> {
                    N("span", Font(36), S("fontWeight", "700", "lineHeight", "34px"), undone.ToString(CultureInfo.InvariantCulture)),
                    N("span", Font(13), S("lineHeight", "16px"), "条待完成"),
                    N("span", Font(12), S("lineHeight", "15px", "color", "#000000"),
                      "全部 " + total + " 条 · 已完成 " + doneCount)
                });
            }
            else if (mode == "empty")
            {
                body = N("div", "flex flex-col flex-1 items-center justify-center min-h-0", S("gap", "4px"), new List<object> {
                    N("span", Font(16), S("fontWeight", "700"), "暂无待办"),
                    N("span", Font(12), S("color", "#000000"), "在左边加一条，然后同步")
                });
            }
            else
            {
                var rows = new List<object>();
                foreach (var t in items) rows.Add(Row(t, now, L));
                body = N("div", "flex flex-col flex-1 min-h-0 overflow-hidden",
                    S("marginTop", "4px", "gap", L.gap + "px"), rows);
            }

            var footer = N("div", "flex flex-row items-center justify-between shrink-0",
                S("marginTop", "4px", "height", "13px"), new List<object> {
                    N("span", Font(11), S("color", "#000000", "lineHeight", "13px"), (index + 1) + " / " + pages),
                    N("span", Font(11), S("color", "#000000", "lineHeight", "13px"),
                      showClock ? ("完成 " + doneCount + "/" + total) : (undone + " 条待完成"))
                });

            var root = N("div", "flex flex-col w-full h-full bg-white text-black",
                S("padding", PAD_Y + "px " + PAD_X + "px", "boxSizing", "border-box"),
                new List<object> { header, bar, body, footer });

            return new Dictionary<string, object> { { "default", new List<object> { root } } };
        }
    }

    // ---------------------------------------------------------------- API
    public class ApiResult
    {
        public bool Ok;
        public int Status;
        public string Body = "";
        public string Error = "";
    }

    public static class Api
    {
        public const string BaseUrl = "https://dot.mindreset.tech/api/authV2/open/device/";

        static Api()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        public static ApiResult Send(string apiKey, string deviceId, string path, string method, string json)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(BaseUrl + Uri.EscapeDataString(deviceId) + path);
                req.Method = method;
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                req.Headers["Authorization"] = "Bearer " + apiKey;
                req.UserAgent = "QuoteTodo/1.0";

                if (json != null)
                {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    req.ContentType = "application/json; charset=utf-8";
                    req.ContentLength = bytes.Length;
                    using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return new ApiResult { Ok = true, Status = (int)resp.StatusCode, Body = sr.ReadToEnd() };
                }
            }
            catch (WebException we)
            {
                int status = 0;
                string body = "";
                var hr = we.Response as HttpWebResponse;
                if (hr != null)
                {
                    status = (int)hr.StatusCode;
                    try
                    {
                        using (var sr = new StreamReader(hr.GetResponseStream(), Encoding.UTF8)) body = sr.ReadToEnd();
                    }
                    catch { }
                }
                return new ApiResult { Ok = false, Status = status, Body = body, Error = we.Message };
            }
            catch (Exception ex)
            {
                return new ApiResult { Ok = false, Status = 0, Error = ex.Message };
            }
        }

        public static string Explain(ApiResult r)
        {
            string msg = "";
            try
            {
                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.Body);
                if (d != null && d.ContainsKey("message")) msg = Convert.ToString(d["message"]);
            }
            catch { }

            if (r.Status == 401) return "密钥无效或已过期（401）";
            if (r.Status == 403) return "这个密钥没有该设备的权限（403）";
            if (msg.IndexOf("API key", StringComparison.OrdinalIgnoreCase) >= 0 &&
                msg.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0)
                return "API 密钥无效，去 Dot App 重新取一个";
            if (r.Status == 404) return "循环列表里找不到「画板 API」卡片，或 taskKey 不匹配（404）";
            if (r.Status == 400) return "参数有问题（400）" + (msg != "" ? "：" + msg : "");
            if (r.Status == 500) return "设备响应失败（500），看看是不是没接电/离线";
            if (r.Status == 0) return "网络请求失败：" + r.Error;
            return "HTTP " + r.Status + (msg != "" ? "：" + msg : "");
        }
    }

    // ---------------------------------------------------------------- 推送设置
    public class CardItem
    {
        public string Key = "";
        public string Label = "";
        public override string ToString() { return Label; }
    }

    public class SettingsDialog : Window
    {
        readonly AppData _data;
        PasswordBox _key;
        TextBox _keyPlain;
        TextBox _device;
        TextBlock _status;
        ComboBox _pages, _delay;
        CheckBox _auto;
        StackPanel _bindHost;
        readonly List<CardItem> _cards = new List<CardItem>();
        readonly List<ComboBox> _boxes = new List<ComboBox>();

        public SettingsDialog(AppData data)
        {
            _data = data;
            Build();
        }

        static TextBlock Lbl(string t)
        {
            return new TextBlock
            {
                Text = t, FontFamily = T.Cjk, FontSize = 11.5, Foreground = T.B(T.Muted),
                Margin = new Thickness(0, 12, 0, 4)
            };
        }

        static Button Btn(string t, bool primary)
        {
            var b = new Button
            {
                Content = t, FontFamily = T.Cjk, FontSize = 13,
                Padding = new Thickness(14, 6, 14, 6),
                Background = primary ? T.B(T.Ink) : T.B(Colors.White),
                Foreground = primary ? T.B(Colors.White) : T.B(T.Ink),
                BorderBrush = T.BA(40, Colors.Black),
                BorderThickness = new Thickness(1)
            };
            return b;
        }

        void Build()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.Height;
            Width = 560;
            ShowInTaskbar = false;
            Topmost = true;

            var card = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = T.B(Colors.White),
                BorderBrush = T.BA(30, Colors.Black),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(22, 20, 22, 18),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 5, Opacity = 0.22, Color = Colors.Black }
            };
            TextOptions.SetTextFormattingMode(card, TextFormattingMode.Display);

            var root = new StackPanel();
            card.Child = root;

            var head = new StackPanel();
            head.Children.Add(new TextBlock
            {
                Text = "推送设置", FontFamily = T.Cjk, FontSize = 16, FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.Ink)
            });
            head.Children.Add(new TextBlock
            {
                Text = "改动后自动推到 Quote/0。密钥用 Windows DPAPI 加密后存本机。",
                FontFamily = T.Cjk, FontSize = 11.5, Foreground = T.B(T.Muted),
                Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            head.MouseLeftButtonDown += (s, e) => { try { DragMove(); } catch { } };
            root.Children.Add(head);

            root.Children.Add(Lbl("API 密钥"));
            var keyRow = new Grid();
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string savedKey = Store.Unprotect(_data.S.ApiKeyEnc);
            _key = new PasswordBox { Password = savedKey, FontSize = 13, Padding = new Thickness(6, 4, 6, 4) };
            _keyPlain = new TextBox
            {
                Text = savedKey, FontSize = 13, Padding = new Thickness(6, 4, 6, 4),
                Visibility = Visibility.Collapsed
            };
            var keyStack = new Grid();
            keyStack.Children.Add(_key);
            keyStack.Children.Add(_keyPlain);

            var showKey = Btn("显示", false);
            showKey.Margin = new Thickness(8, 0, 0, 0);
            showKey.Click += (s, e) =>
            {
                bool hidden = _key.Visibility == Visibility.Visible;
                if (hidden) { _keyPlain.Text = _key.Password; _key.Visibility = Visibility.Collapsed; _keyPlain.Visibility = Visibility.Visible; }
                else { _key.Password = _keyPlain.Text; _keyPlain.Visibility = Visibility.Collapsed; _key.Visibility = Visibility.Visible; }
                showKey.Content = hidden ? "隐藏" : "显示";
            };
            Grid.SetColumn(showKey, 1);
            keyRow.Children.Add(keyStack);
            keyRow.Children.Add(showKey);
            root.Children.Add(keyRow);

            root.Children.Add(Lbl("设备序列号"));
            _device = new TextBox { Text = _data.S.DeviceId, FontSize = 13, Padding = new Thickness(6, 4, 6, 4) };
            root.Children.Add(_device);

            var act = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var test = Btn("测试连接", false);
            var discover = Btn("发现画板卡", false);
            discover.Margin = new Thickness(8, 0, 0, 0);
            test.Click += (s, e) => TestConn();
            discover.Click += (s, e) => Discover();
            act.Children.Add(test);
            act.Children.Add(discover);
            root.Children.Add(act);

            _status = new TextBlock
            {
                FontFamily = T.Cjk, FontSize = 11.5, Foreground = T.B(T.Muted),
                Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MinHeight = 16
            };
            root.Children.Add(_status);

            var opts = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            opts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            opts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var p1 = new StackPanel();
            p1.Children.Add(Lbl("轮播页数"));
            _pages = new ComboBox { Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 1; i <= 5; i++) _pages.Items.Add(i);
            _pages.SelectedItem = Math.Max(1, Math.Min(5, _data.S.Pages));
            _pages.SelectionChanged += (s, e) => RebuildBindings();
            p1.Children.Add(_pages);

            var p2 = new StackPanel();
            p2.Children.Add(Lbl("自动推送"));
            _auto = new CheckBox { Content = "改动后自动推", IsChecked = _data.S.AutoPush, FontFamily = T.Cjk, FontSize = 12.5 };
            p2.Children.Add(_auto);

            var p3 = new StackPanel();
            p3.Children.Add(Lbl("防抖延迟"));
            _delay = new ComboBox { Width = 110, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (int d in new[] { 10, 20, 30, 60 }) _delay.Items.Add(d);
            _delay.SelectedItem = _delay.Items.Contains(_data.S.PushDelay) ? _data.S.PushDelay : 20;
            p3.Children.Add(_delay);

            Grid.SetColumn(p1, 0); Grid.SetColumn(p2, 1); Grid.SetColumn(p3, 2);
            opts.Children.Add(p1); opts.Children.Add(p2); opts.Children.Add(p3);
            root.Children.Add(opts);

            root.Children.Add(Lbl("每页对应的画板卡"));
            _bindHost = new StackPanel();
            root.Children.Add(_bindHost);
            RebuildBindings();

            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = Btn("取消", false);
            var save = Btn("保存", true);
            save.Margin = new Thickness(8, 0, 0, 0);
            cancel.Click += (s, e) => DialogResult = false;
            save.Click += (s, e) => SaveAndClose();
            foot.Children.Add(cancel);
            foot.Children.Add(save);
            root.Children.Add(foot);

            Content = card;
        }

        void SetStatus(string s)
        {
            if (_status != null) _status.Text = s;
        }

        void RebuildBindings()
        {
            if (_bindHost == null || _pages == null) return;
            int count = (int)(_pages.SelectedItem ?? 3);
            _bindHost.Children.Clear();
            _boxes.Clear();

            for (int i = 0; i < count; i++)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
                row.Children.Add(new TextBlock
                {
                    Text = "第 " + (i + 1) + " 页", FontFamily = T.Cjk, FontSize = 12.5,
                    Width = 62, VerticalAlignment = VerticalAlignment.Center, Foreground = T.B(T.Ink)
                });

                var cb = new ComboBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12.5 };
                cb.Items.Add(new CardItem { Key = "", Label = "— 未绑定 —" });
                foreach (var c in _cards) cb.Items.Add(c);

                string want = i < _data.S.Bindings.Count ? _data.S.Bindings[i] : "";
                cb.SelectedIndex = 0;
                for (int k = 1; k < cb.Items.Count; k++)
                {
                    var it = cb.Items[k] as CardItem;
                    if (it != null && it.Key == want) { cb.SelectedIndex = k; break; }
                }
                row.Children.Add(cb);
                _bindHost.Children.Add(row);
                _boxes.Add(cb);
            }
        }

        void Apply()
        {
            string keyText = (_key.Visibility == Visibility.Visible ? _key.Password : _keyPlain.Text).Trim();
            _data.S.ApiKeyEnc = Store.Protect(keyText);
            _data.S.DeviceId = _device.Text.Trim();
            _data.S.Pages = (int)(_pages.SelectedItem ?? 3);
            _data.S.AutoPush = _auto.IsChecked == true;
            _data.S.PushDelay = (int)(_delay.SelectedItem ?? 20);

            var list = new List<string>();
            foreach (var cb in _boxes)
            {
                var it = cb.SelectedItem as CardItem;
                list.Add(it == null ? "" : it.Key);
            }
            _data.S.Bindings = list;
        }

        void SaveAndClose()
        {
            Apply();
            DialogResult = true;
        }

        void TestConn()
        {
            Apply();
            SetStatus("连接中…");
            var key = Store.Unprotect(_data.S.ApiKeyEnc);
            var dev = _data.S.DeviceId;
            Task.Run(() =>
            {
                var r = Api.Send(key, dev, "/status", "GET", null);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsVisible) return;
                    if (!r.Ok) { SetStatus("✗ " + Api.Explain(r)); return; }
                    try
                    {
                        var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(r.Body);
                        var st = d.ContainsKey("status") ? d["status"] as Dictionary<string, object> : null;
                        string battery = st != null && st.ContainsKey("battery") ? Convert.ToString(st["battery"]) : "?";
                        string wifi = st != null && st.ContainsKey("wifi") ? Convert.ToString(st["wifi"]) : "?";
                        SetStatus("✓ 已连接 · " + battery + " · " + wifi);
                    }
                    catch { SetStatus("✓ 已连接"); }
                }));
            });
        }

        void Discover()
        {
            Apply();
            SetStatus("读取循环列表…");
            var key = Store.Unprotect(_data.S.ApiKeyEnc);
            var dev = _data.S.DeviceId;
            Task.Run(() =>
            {
                var r = Api.Send(key, dev, "/loop/list", "GET", null);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsVisible) return;
                    if (!r.Ok) { SetStatus("✗ " + Api.Explain(r)); return; }

                    var cards = new List<CardItem>();
                    try
                    {
                        var arr = new JavaScriptSerializer().DeserializeObject(r.Body);
                        foreach (var o in Store.AsList(arr))
                        {
                            var m = o as Dictionary<string, object>;
                            if (m == null) continue;
                            string type = m.ContainsKey("type") ? Convert.ToString(m["type"]) : "";
                            if (type != "CANVAS_API") continue;
                            string k = m.ContainsKey("key") ? Convert.ToString(m["key"]) : "";
                            if (string.IsNullOrEmpty(k)) continue;
                            string alias = m.ContainsKey("alias") ? Convert.ToString(m["alias"]) : k;
                            cards.Add(new CardItem { Key = k, Label = alias + "  ·  " + (k.Length > 8 ? k.Substring(0, 8) : k) });
                        }
                    }
                    catch { }

                    _cards.Clear();
                    _cards.AddRange(cards);
                    RebuildBindings();

                    if (cards.Count == 0)
                        SetStatus("✗ 循环列表里没有「画板 API」卡片。\nDot App → 内容工坊 → 循环列表 → 添加「画板 API」");
                    else
                        SetStatus("✓ 找到 " + cards.Count + " 个画板卡，已按顺序填入");
                }));
            });
        }
    }

    // ---------------------------------------------------------------- entry
    public static class Program
    {
        public static void LogFatal(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Store.Dir);
                File.AppendAllText(Path.Combine(Store.Dir, "error.log"),
                    DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            if (args.Length >= 2 && args[0] == "--snapshot")
            {
                Snapshot.Save(args[1]);
                return;
            }

            if (args.Length >= 2 && args[0] == "--selftest")
            {
                var log = new StringBuilder();
                try
                {
                    var d = Store.Load();
                    log.AppendLine("loaded=" + d.Todos.Count);
                    d.Todos.Add(new Todo { Id = Guid.NewGuid().ToString("N"), Title = "自检条目", Order = d.Todos.Count });
                    Store.Save(d);
                    var raw = File.ReadAllText(Store.FilePath, Encoding.UTF8);
                    log.AppendLine("rawlen=" + raw.Length);
                    var ser = new JavaScriptSerializer();
                    var root = ser.Deserialize<Dictionary<string, object>>(raw);
                    object tv;
                    root.TryGetValue("todos", out tv);
                    log.AppendLine("todos_type=" + (tv == null ? "null" : tv.GetType().FullName));
                    if (tv is object[])
                    {
                        var arr = (object[])tv;
                        log.AppendLine("todos_len=" + arr.Length);
                        if (arr.Length > 0) log.AppendLine("item_type=" + arr[0].GetType().FullName);
                    }
                    var d2 = Store.Load();
                    log.AppendLine("reloaded=" + d2.Todos.Count);
                    var w = new Widget();
                    var card = w.BuildForSnapshot(d2);
                    card.Measure(new Size(320, double.PositiveInfinity));
                    card.Arrange(new Rect(0, 0, 320, card.DesiredSize.Height));
                    log.AppendLine("card=" + 320 + "x" + Math.Round(card.DesiredSize.Height));
                    log.AppendLine("file=" + Store.FilePath);
                    log.AppendLine("OK");
                }
                catch (Exception ex)
                {
                    log.AppendLine("FAIL " + ex);
                }
                File.WriteAllText(args[1], log.ToString(), Encoding.UTF8);
                return;
            }

            if (args.Length >= 2 && args[0] == "--pushdry")
            {
                var now = new DateTime(2026, 10, 28, 19, 40, 0);
                var items = new List<Todo> {
                    new Todo { Title = "把原型发给老王", Due = "2026-10-26" },
                    new Todo { Title = "改看板字号，16px 看着舒服", Due = "2026-10-28" },
                    new Todo { Title = "确认透明度够不够", Due = "2026-10-29", Done = true }
                };
                var wd = Board.BuildWindowData(0, 3, 3, items, 3, 1, 2, "list", false, now);
                var payload = new Dictionary<string, object>
                {
                    {"taskType", "loop"}, {"refreshNow", true}, {"taskAlias", "待办看板 P1"},
                    {"data", new Dictionary<string, object>
                        {
                            {"page", 1}, {"pages", 3}, {"total", 3}, {"done", 1}, {"undone", 2},
                            {"generatedAt", now.ToString("o")},
                            {"items", items.Select(t => new Dictionary<string, object>
                                {{"title", t.Title}, {"done", t.Done}, {"due", t.Due}}).ToList()}
                        }},
                    {"windowData", wd}, {"layoutFull", Board.LayoutFull}, {"border", 0}
                };
                File.WriteAllText(args[1], new JavaScriptSerializer().Serialize(payload), new UTF8Encoding(false));
                return;
            }

            if (args.Length >= 2 && args[0] == "--apitest")
            {
                var key = Environment.GetEnvironmentVariable("QT_KEY") ?? "";
                var dev = Environment.GetEnvironmentVariable("QT_DEVICE") ?? "";
                var r = Api.Send(key, dev, "/status", "GET", null);
                File.WriteAllText(args[1],
                    "ok=" + r.Ok + "\nstatus=" + r.Status + "\nbody=" + r.Body + "\nerror=" + r.Error + "\nexplain=" + Api.Explain(r),
                    new UTF8Encoding(false));
                return;
            }

            if (args.Length >= 2 && args[0] == "--uicheck")
            {
                try
                {
                    var d = Store.Load();
                    var dlg = new SettingsDialog(d);
                    var card = (FrameworkElement)dlg.Content;
                    dlg.Content = null;              // 先从 Window 上摘下来，否则不能挂到别的父节点
                    card.Margin = new Thickness(30);

                    var host = new Grid
                    {
                        Width = 620, Height = 860,
                        Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xE9, 0xE2))
                    };
                    host.Children.Add(card);
                    host.Measure(new Size(620, 860));
                    host.Arrange(new Rect(0, 0, 620, 860));
                    host.UpdateLayout();

                    var rtb = new RenderTargetBitmap(620, 860, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(host);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using (var fs = File.Create(args[1])) enc.Save(fs);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1] + ".err.txt", "dialog FAIL " + ex, new UTF8Encoding(false));
                }
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogFatal(e.ExceptionObject as Exception);
            app.DispatcherUnhandledException += (s, e) => { LogFatal(e.Exception); };

            bool created;
            var mutex = new Mutex(true, "QuoteTodo.SingleInstance", out created);
            if (!created) return;

            try
            {
                var w = new Widget();
                w.Start();
                app.Run();
            }
            catch (Exception ex)
            {
                LogFatal(ex);
            }
            GC.KeepAlive(mutex);
        }
    }
}
