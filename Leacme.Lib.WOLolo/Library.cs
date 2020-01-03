// Copyright (c) 2017 Leacme (http://leac.me). View LICENSE.md for more information.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LiteDB;
using PacketDotNet;

namespace Leacme.Lib.WOLolo {

	public class Library {

		private LiteDatabase db = new LiteDatabase(typeof(Library).Namespace + ".Settings.db");
		private LiteCollection<BsonDocument> ipMacPairs;
		private IList<IPAddress> cachedEnabledIPv4LocalInterfaces = GetEnabledIPv4LocalInterfaces();

		public Library() {
			ipMacPairs = db.GetCollection(nameof(ipMacPairs));
		}

		public bool IsDbEmpty() {
			return ipMacPairs.Count().Equals(0);
		}

		/// <summary>
		/// Broadcasts a Wake On Lan packet to a device on the network to wake it up.
		/// /// </summary>
		/// <param name="macOfDevice">the media access control address of the device.</param>
		public void BroadcastWOLPacketToDevice(PhysicalAddress macOfDevice) {
			BroadcastDataOnLocalInterfaces(cachedEnabledIPv4LocalInterfaces.ToList(), new WakeOnLanPacket(macOfDevice).Bytes);
		}

		/// <summary>
		///	Populates the database with newly retrieved entries from the network ARP table.
		/// /// </summary>
		/// <returns></returns>
		public async Task PopulateDbWithCurrentArpTable() {
			var ipsAndMacs = (await GetARPTableAddressesAndMacs()).ToList();
			db.DropCollection(nameof(ipMacPairs));
			ipsAndMacs.ForEach(z => {
				ipMacPairs.Insert(new BsonDocument { [nameof(z.networkAddress)] = z.networkAddress.ToString(), [nameof(z.macAddress)] = z.macAddress.ToString() });
			});
		}

		/// <summary>
		/// Retrieves stored IP address - MAC address pairs from the database cache.
		/// /// </summary>
		/// <param name="networkAddress"></param>
		/// <param name="macAddress"></param>
		/// <returns></returns>
		public IList<(IPAddress networkAddress, PhysicalAddress macAddress)> GetStoredIPMacPairs() {
			var ipMacPairs = new List<(IPAddress networkAddress, PhysicalAddress macAddress)>();
			foreach (var iMpair in this.ipMacPairs.FindAll()) {
				ipMacPairs.Add((IPAddress.Parse(iMpair["networkAddress"]), PhysicalAddress.Parse(iMpair["macAddress"])));
			}
			return ipMacPairs;
		}

		/// <summary>
		/// Queries and returns a list of all local IPv4 network interfaces which are enabled.
		/// /// </summary>
		/// <returns></returns>
		public static IList<IPAddress> GetEnabledIPv4LocalInterfaces() {
			List<IPAddress> ips = new List<IPAddress>();
			foreach (var intf in NetworkInterface.GetAllNetworkInterfaces()) {
				if (intf.OperationalStatus.Equals(OperationalStatus.Up) && intf.SupportsMulticast && intf.GetIPProperties().GetIPv4Properties() != null && !intf.GetIPProperties().GetIPv4Properties().Index.Equals(NetworkInterface.LoopbackInterfaceIndex)) {
					foreach (var unicastIP in intf.GetIPProperties().UnicastAddresses) {
						if (unicastIP.Address.AddressFamily.Equals(AddressFamily.InterNetwork)) {
							ips.Add(unicastIP.Address);
						}
					}

				}
			}
			return ips;
		}

		/// <summary>
		/// Broadcasts data on all selected local interfaces to their networks.
		/// /// </summary>
		/// <param name="localInterfaceIPs"></param>
		/// <param name="data"></param>
		public void BroadcastDataOnLocalInterfaces(List<IPAddress> localInterfaceIPs, byte[] data) {
			foreach (var localIP in localInterfaceIPs) {
				var iPend = new IPEndPoint(BitConverter.ToUInt32(localIP.GetAddressBytes(), 0), 0);
				var client = new UdpClient(iPend);
				client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, 1);
				client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontRoute, 1);
				var remoteEpt = new IPEndPoint(IPAddress.Broadcast, 7331);
				client.Send(data, data.Length, remoteEpt);
				client.Close();
			}
		}

		/// <summary>
		/// Pings and checks if the device is online and is reachable on the network.
		/// /// </summary>
		/// <param name="networkAddress"></param>
		/// <returns></returns>
		public async Task<PhysicalAddress> GetMacFromIPIfDeviceIsOnline(IPAddress networkAddress) {
			var deviceOnline = await Task.Run(() => new Ping().Send(networkAddress, 1000)?.Status.Equals(IPStatus.Success) == true);
			var currentARPTable = await GetARPTableAddressesAndMacs();
			(IPAddress networkAddress, PhysicalAddress macAddress) foundMacAddress;
			try {
				foundMacAddress = currentARPTable.First(z => z.networkAddress.Equals(networkAddress));
			} catch (InvalidOperationException) {
				throw new KeyNotFoundException("Address not found in Address Resolution Protocol table");
			}
			return foundMacAddress.macAddress;
		}

		/// <summary>
		/// Queries the network Address Resolution Protocol table and retrieves its stored IP/MAC address pairs.
		/// /// </summary>
		/// <param name="networkAddress"></param>
		/// <param name="macAddress"></param>
		/// <returns></returns>
		public async Task<IList<(IPAddress networkAddress, PhysicalAddress macAddress)>> GetARPTableAddressesAndMacs() {
			var ipMacPairs = new List<(IPAddress networkAddress, PhysicalAddress macAddress)>();
			Process p = new Process();
			p.StartInfo.FileName = "arp";
			p.StartInfo.Arguments = "-a ";
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.CreateNoWindow = true;
			await Task.Run(() => {
				p.Start();
				string outp = p.StandardOutput.ReadToEnd();
				var lines = outp.Split('\n').Where(z => !string.IsNullOrWhiteSpace(z));
				foreach (var line in lines) {
					var colonMatchOutput = Regex.Match(line, string.Join(":", Enumerable.Repeat("[0-9A-Fa-f]{2}", 6)));
					var dashMatchOutput = Regex.Match(line, string.Join("-", Enumerable.Repeat("[0-9A-Fa-f]{2}", 6)));
					string foundMac;
					string matchingIP;

					if (colonMatchOutput.Success) {
						foundMac = colonMatchOutput.Value.Replace(':', '-').ToUpper();
					} else if (dashMatchOutput.Success) {
						foundMac = dashMatchOutput.Value.ToUpper();
					} else {
						continue;
					}

					Match ipMatch = Regex.Match(line, @"\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}");
					if (ipMatch.Success) {
						matchingIP = ipMatch.Value;
						ipMacPairs.Add((IPAddress.Parse(matchingIP), PhysicalAddress.Parse(foundMac)));
					}
				}
			});
			return ipMacPairs;
		}
	}
}