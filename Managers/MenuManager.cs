using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Interfaces;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Fizzy.ImageViewer.Managers
{
    internal sealed class MenuManager
    {
        private readonly ContextMenu _contextMenu;
        private readonly List<Func<IEnumerable<IMenuItem>>> _registeredItems = [];
        private UIElement? _menuTargetShape;

        public Func<bool> CheckHasSelection { get; set; } = () => false;
        public Func<bool> CheckHasSelectedShape { get; set; } = () => false;
        public Func<UIElement?> GetSelectedShape { get; set; } = () => null;

        /// <summary>
        /// 菜单打开时的回调，用于冻结帧更新。
        /// </summary>
        public Action? OnMenuOpening { get; set; }

        /// <summary>
        /// 菜单关闭时的回调，用于解冻帧更新。
        /// </summary>
        public Action? OnMenuClosing { get; set; }

        public MenuManager(FrameworkElement contextMenuTarget)
        {
            _contextMenu = new ContextMenu();
            contextMenuTarget.ContextMenu = _contextMenu;
            _contextMenu.Opened += OnContextMenuOpened;
            _contextMenu.Closed += OnContextMenuClosed;
        }

        public void Register(IMenuItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _registeredItems.Add(() => [item]);
        }

        internal void RegisterMeasureTools(Func<IEnumerable<IMenuItem>> provider) => _registeredItems.Add(provider);
        internal IMenuItem[] SnapshotItems() => _registeredItems.SelectMany(provider => provider()).ToArray();

        public UIElement? GetMenuTargetShape() => _menuTargetShape;

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            OnMenuOpening?.Invoke();

            _contextMenu.Items.Clear();
            bool isMeasuring = CheckHasSelection();
            bool hasSelectedShape = CheckHasSelectedShape();
            _menuTargetShape = GetSelectedShape();

            bool pendingSeparator = false;
            int visibleCount = 0;

            foreach (var item in SnapshotItems())
            {
                bool isVisible = item.Type switch
                {
                    MenuItemType.General => true,
                    MenuItemType.MeasureTool => !isMeasuring,
                    MenuItemType.ContextAction => isMeasuring,
                    MenuItemType.SelectionAction => hasSelectedShape && !isMeasuring,
                    MenuItemType.Separator => false, // 特殊处理
                    _ => false
                };

                if (item.Type == MenuItemType.Separator)
                {
                    pendingSeparator = true;
                    continue;
                }

                if (!isVisible || !item.IsVisible) continue;

                // 只在已有可见项后才添加分隔符
                if (pendingSeparator && visibleCount > 0)
                {
                    _contextMenu.Items.Add(new Separator());
                }
                pendingSeparator = false; // 无论是否添加分隔符，都重置标志

                var menuItem = new MenuItem { Header = item.Header };

                if (item is ICheckableMenuItem checkable)
                {
                    menuItem.IsCheckable = true;
                    menuItem.IsChecked = checkable.IsChecked;
                }

                menuItem.Click += item.Execute;
                _contextMenu.Items.Add(menuItem);
                visibleCount++;
            }
        }

        private void OnContextMenuClosed(object sender, RoutedEventArgs e)
        {
            OnMenuClosing?.Invoke();
        }
    }
}
