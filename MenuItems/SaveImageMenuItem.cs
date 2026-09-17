using Microsoft.Win32;
using Fizzy.ImageViewer.Controls;
using Fizzy.ImageViewer.Enums;
using Fizzy.ImageViewer.Interfaces;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Fizzy.ImageViewer.MenuItems;

/// <summary>
/// 保存图像菜单项。
/// </summary>
public sealed class SaveImageMenuItem(ImageLayer layer) : IMenuItem
{
    public string Header => "Save Image As...";
    public MenuItemType Type => MenuItemType.General;

    public void Execute(object sender, RoutedEventArgs e)
    {
        if (layer.ImageDisplay.Source is not BitmapSource source)
        {
            MessageBox.Show("No image to save.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap Image|*.bmp",
            FileName = $"Image_{DateTime.Now:yyyyMMdd_HHmmss}_{Stopwatch.GetTimestamp() % 10000:D4}"
        };

        if (sfd.ShowDialog() == true)
        {
            BitmapEncoder encoder = sfd.FilterIndex switch
            {
                1 => new PngBitmapEncoder(),
                2 => new JpegBitmapEncoder(),
                _ => new BmpBitmapEncoder()
            };

            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new FileStream(sfd.FileName, FileMode.Create);
            encoder.Save(stream);
        }
    }
}