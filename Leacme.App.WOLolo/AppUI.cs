// Copyright (c) 2017 Leacme (http://leac.me). View LICENSE.md for more information.
using System.Collections.Generic;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Leacme.Lib.WOLolo;

namespace Leacme.App.WOLolo {

	public class AppUI {

		private StackPanel rootPan = (StackPanel)Application.Current.MainWindow.Content;
		private Library lib = new Library();

		public AppUI() {

			var instBlurb = App.TextBlock;
			instBlurb.TextAlignment = TextAlignment.Center;
			instBlurb.Text = "If you don't see the IP of the device you want to wake up, try entering its IP to refresh list.\nMake sure the device is online when doing this.";

			var arpValues = App.ComboBoxWithLabel;
			arpValues.label.Text = "Device to wake up:";
			arpValues.comboBox.Width = 230;

			var refrListBt = App.Button;
			refrListBt.Content = "Refresh";
			refrListBt.Click += (z, zz) => PopulateDbAndArmMenuWithCurrentNetworkEntries(arpValues);
			arpValues.holder.Children.Add(refrListBt);

			var addIpControls = App.HorizontalFieldWithButton;
			addIpControls.label.Text = "IP to add:";
			addIpControls.field.Watermark = "192.168.1.100";
			addIpControls.field.Width = 200;
			addIpControls.button.Content = "Add device IP";
			addIpControls.button.Click += async (z, zz) => {
				try {
					((App)Application.Current).LoadingBar.IsIndeterminate = true;
					var newIp = IPAddress.Parse(addIpControls.field.Text);
					await lib.GetMacFromIPIfDeviceIsOnline(newIp);
					PopulateDbAndArmMenuWithCurrentNetworkEntries(arpValues);
				} catch {
					addIpControls.field.Text = "";
					((App)Application.Current).LoadingBar.IsIndeterminate = false;
				}
				((App)Application.Current).LoadingBar.IsIndeterminate = false;
			};

			var horCtrlHolder1 = App.HorizontalStackPanel;
			horCtrlHolder1.HorizontalAlignment = HorizontalAlignment.Center;
			horCtrlHolder1.Children.AddRange(new List<IControl> { arpValues.holder, addIpControls.holder });
			PopulateArmMenuFromDb(arpValues);
			var wakeBt = WakeButton.WakeBt;
			wakeBt.PointerReleased += ((z, zz) => {
				lib.BroadcastWOLPacketToDevice((((IPAddress networkAddress, System.Net.NetworkInformation.PhysicalAddress macAddress))arpValues.comboBox.SelectedItem).macAddress);
			});
			rootPan.Children.AddRange(new List<IControl> { instBlurb, horCtrlHolder1, wakeBt });

			if (lib.IsDbEmpty()) {
				PopulateDbAndArmMenuWithCurrentNetworkEntries(arpValues);
			}
		}

		private void PopulateDbAndArmMenuWithCurrentNetworkEntries((StackPanel holder, TextBlock label, ComboBox comboBox) arpValues) {
			Dispatcher.UIThread.InvokeAsync(async () => {
				((App)Application.Current).LoadingBar.IsIndeterminate = true;
				await lib.PopulateDbWithCurrentArpTable();
				PopulateArmMenuFromDb(arpValues);
				((App)Application.Current).LoadingBar.IsIndeterminate = false;
			});
		}

		private void PopulateArmMenuFromDb((StackPanel holder, TextBlock label, ComboBox comboBox) arpValues) {
			arpValues.comboBox.Items = lib.GetStoredIPMacPairs();
			arpValues.comboBox.SelectedIndex = 0;
		}
	}
}