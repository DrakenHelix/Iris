using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Accessibility;

namespace Iris {
    static class ChromeDownloadMsaa {
        const int DocumentRole = 15;
        const int StaticTextRole = 41;
        const int TextRole = 42;
        const int CellRole = 29;
        const int InvisibleOrOffscreen = 0x18000;

        sealed class Node {
            public IAccessible Element;
            public int ChildId;
            public int Depth;
        }

        public static SteamCounterValue Read(IntPtr window, ChromeReadTrace trace) {
            var renderers = new List<IntPtr>();
            EnumChildWindows(window, delegate(IntPtr child, IntPtr ignored) {
                var name = new StringBuilder(96);
                if (IsWindowVisible(child) && GetClassName(child, name, name.Capacity) > 0
                    && name.ToString() == "Chrome_RenderWidgetHostHWND") renderers.Add(child);
                return renderers.Count < 8;
            }, IntPtr.Zero);
            renderers.Add(window);

            foreach (IntPtr handle in renderers) {
                IAccessible root;
                Guid iid = typeof(IAccessible).GUID;
                if (AccessibleObjectFromWindow(handle, unchecked((uint)-4), ref iid, out root) != 0 || root == null) continue;
                foreach (var node in Walk(root, 180)) {
                    if (Role(node) != DocumentRole || !Visible(node)) continue;
                    // Never inspect text until Chrome identifies this document as its internal downloads page.
                    if (!ChromeDownloadCounter.IsDownloadsPage(node.Element.get_accValue(node.ChildId))) continue;
                    if (trace != null) trace.MsaaPages++;
                    var document = node.ChildId == 0 ? node.Element : node.Element.get_accChild(node.ChildId) as IAccessible;
                    if (document != null) return ReadDocument(document, trace);
                }
            }
            return null;
        }

        static SteamCounterValue ReadDocument(IAccessible document, ChromeReadTrace trace) {
            var lines = new HashSet<string>(StringComparer.Ordinal);
            SteamCounterValue found = null;
            foreach (var node in Walk(document, 260)) {
                int role = Role(node);
                if ((role != StaticTextRole && role != TextRole && role != CellRole) || !Visible(node)) continue;
                string text = node.Element.get_accName(node.ChildId);
                var sample = ChromeDownloadCounter.Parse(text, DateTime.UtcNow);
                if (sample == null || !lines.Add(text)) continue;
                if (trace != null) trace.MsaaLines++;
                if (found != null) return null;
                found = sample;
            }
            return found;
        }

        static int Role(Node node) {
            var value = node.Element.get_accRole(node.ChildId);
            return value is int ? (int)value : 0;
        }

        static bool Visible(Node node) {
            var value = node.Element.get_accState(node.ChildId);
            return value is int && ((int)value & InvisibleOrOffscreen) == 0;
        }

        static IEnumerable<Node> Walk(IAccessible root, int maximum) {
            var queue = new Queue<Node>();
            queue.Enqueue(new Node { Element = root });
            for (int seen = 0; queue.Count > 0 && seen < maximum; seen++) {
                var node = queue.Dequeue();
                yield return node;
                if (node.ChildId != 0 || node.Depth >= 14 || queue.Count + seen >= maximum) continue;
                int count;
                try { count = Math.Min(node.Element.accChildCount, Math.Min(48, maximum - seen - queue.Count)); }
                catch (COMException) { continue; }
                if (count <= 0) continue;
                var children = new object[count];
                int actual;
                if (AccessibleChildren(node.Element, 0, count, children, out actual) < 0) continue;
                for (int index = 0; index < actual && index < children.Length; index++) {
                    var element = children[index] as IAccessible;
                    if (element != null) queue.Enqueue(new Node { Element = element, Depth = node.Depth + 1 });
                    else if (children[index] is int && (int)children[index] > 0)
                        queue.Enqueue(new Node { Element = node.Element, ChildId = (int)children[index], Depth = node.Depth + 1 });
                }
            }
        }

        delegate bool WindowCallback(IntPtr window, IntPtr ignored);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, WindowCallback callback, IntPtr ignored);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
        [DllImport("oleacc.dll")] static extern int AccessibleChildren(IAccessible parent, int start, int count,
            [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 2)] object[] children, out int actual);
    }
}
