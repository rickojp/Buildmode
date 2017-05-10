﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Timers;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;

namespace BuildMode
{
	[ApiVersion(2, 1)]
	public class BuildMode : TerrariaPlugin
	{
		public override string Author
		{
			get { return "MarioE"; }
		}
		public override string Description
		{
			get { return "Adds a building command."; }
		}
		public override string Name
		{
			get { return "BuildMode"; }
		}
		Timer Timer = new Timer(1000);
		public override Version Version
		{
			get { return Assembly.GetExecutingAssembly().GetName().Version; }
		}

		public BuildMode(Main game)
			: base(game)
		{
			Order = 10;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				Timer.Dispose();

				ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
				ServerApi.Hooks.NetSendBytes.Deregister(this, OnSendBytes);
				ServerApi.Hooks.NetGreetPlayer.Deregister(this, OnGreet);
			}
		}
		public override void Initialize()
		{
			Commands.ChatCommands.Add(new Command("buildmode.use", BuildModeCmd, "buildmode"));
			Commands.ChatCommands.Add(new Command("buildmode.check", BMCheck, "bmcheck"));

			ServerApi.Hooks.NetGetData.Register(this, OnGetData);
			ServerApi.Hooks.NetSendBytes.Register(this, OnSendBytes);
			ServerApi.Hooks.NetGreetPlayer.Register(this, OnGreet);

			Timer.Elapsed += OnElapsed;
			Timer.Start();
		}

		void OnGreet(GreetPlayerEventArgs args)
		{
			var plr = TShock.Players[args.Who];

			if (plr == null || !plr.Active)
				return;

			plr.SetData("buildmode", false);
		}

		void OnElapsed(object sender, ElapsedEventArgs e)
		{
			for (int i = 0; i < TShock.Players.Length; i++)
			{
				if (TShock.Players[i] == null || !TShock.Players[i].Active || !TShock.Players[i].IsLoggedIn)
					continue;

				if (TShock.Players[i].GetData<bool>("buildmode"))
				{
					Player plr = Main.player[i];
					TSPlayer tsplr = TShock.Players[i];

					if (plr.hostile)
					{
						tsplr.SendErrorMessage("You cannot use Buildmode when PvP is active!");
						TShock.Players[i].SetData("buildmode", false);
						return;
					}

					NetMessage.SendData(7, i);
					if (plr.statLife < plr.statLifeMax && !plr.dead)
					{
						tsplr.Heal(plr.statLifeMax - plr.statLife);
						plr.statLife = plr.statLifeMax;
					}
					tsplr.SetBuff(3, Int16.MaxValue); // Swiftness
					tsplr.SetBuff(11, Int16.MaxValue); // Shine
					tsplr.SetBuff(12, Int16.MaxValue); // Night owl
					tsplr.SetBuff(63, Int16.MaxValue); // Panic
					tsplr.SetBuff(104, Int16.MaxValue); // Mining
					tsplr.SetBuff(107, Int16.MaxValue); // Builder
					tsplr.SetBuff(113, Int16.MaxValue); // Lifeforce
				}
			}
		}
		void OnGetData(GetDataEventArgs e)
		{
			if (!e.Handled && TShock.Players[e.Msg.whoAmI].GetData<bool>("buildmode"))
			{
				Player plr = Main.player[e.Msg.whoAmI];
				TSPlayer tsplr = TShock.Players[e.Msg.whoAmI];

				switch (e.MsgID)
				{
					case PacketTypes.PlayerDamage:
						{
							tsplr.Heal(BitConverter.ToInt16(e.Msg.readBuffer, e.Index + 2));
							plr.statLife = plr.statLifeMax;
						}
						break;
					case PacketTypes.Teleport:
						if ((e.Msg.readBuffer[e.Index] & 1) == 0 && (e.Msg.readBuffer[e.Index] & 2) != 2)
						{
							for (int i = 0; i < Player.maxBuffs; i++)
							{
								if (plr.buffType[i] == 88 && plr.buffTime[i] > 0)
								{
									tsplr.Heal(100);
								}
							}
						}
						break;
					case PacketTypes.PaintTile:
					case PacketTypes.PaintWall:
						{
							int count = 0;
							int type = e.Msg.readBuffer[e.Index + 8];

							Item lastItem = null;
							foreach (Item i in plr.inventory)
							{
								if (i.paint == type)
								{
									lastItem = i;
									count += i.stack;
								}
							}
							if (count <= 5 && lastItem != null)
								tsplr.GiveItem(lastItem.type, lastItem.Name, plr.width, plr.height, lastItem.maxStack + 1 - count);
						}
						break;
					case PacketTypes.Tile:
						{
							int count = 0;
							int type = e.Msg.readBuffer[e.Index];
							if ((type == 1 || type == 3) && plr.inventory[plr.selectedItem].type != 213)
							{
								int tile = e.Msg.readBuffer[e.Index + 9];
								if (tsplr.SelectedItem.tileWand > 0)
									tile = tsplr.SelectedItem.tileWand;
								Item lastItem = null;
								foreach (Item i in plr.inventory)
								{
									if ((type == 1 && i.createTile == tile) || (type == 3 && i.createWall == tile))
									{
										lastItem = i;
										count += i.stack;
									}
								}
								if (count <= 5 && lastItem != null)
									tsplr.GiveItem(lastItem.type, lastItem.Name, plr.width, plr.height, lastItem.maxStack + 1 - count);
							}
							else if (type == 5 || type == 10 || type == 12)
							{
								foreach (Item i in plr.inventory)
								{
									if (i.type == 530)
										count += i.stack;
								}
								if (count <= 5)
									tsplr.GiveItem(530, "Wire", plr.width, plr.height, 1000 - count);
							}
							else if (type == 8)
							{
								foreach (Item i in plr.inventory)
								{
									if (i.type == 849)
										count += i.stack;
								}
								if (count <= 5)
									tsplr.GiveItem(849, "Actuator", plr.width, plr.height, 1000 - count);
							}
						}
						break;
				}
			}
		}
		void OnSendBytes(SendBytesEventArgs e)
		{
			if (TShock.Players[e.Socket.Id] == null)
				return;

			bool build = TShock.Players[e.Socket.Id].GetData<bool>("buildmode");
			switch (e.Buffer[2])
			{
				case 7:
					using (var writer = new BinaryWriter(new MemoryStream(e.Buffer, 3, e.Count - 3)))
					{
						writer.Write(build ? 27000 : (int)Main.time);
						BitsByte bb = 0;
						bb[0] = build ? true : Main.dayTime;
						bb[1] = build ? false : Main.bloodMoon;
						bb[2] = build ? false : Main.eclipse;
						writer.Write(bb);

						writer.BaseStream.Position += 9;
						writer.Write(build ? (short)Main.maxTilesY : (short)Main.worldSurface);
						writer.Write(build ? (short)Main.maxTilesY : (short)Main.rockLayer);

						writer.BaseStream.Position += 4;
						writer.Write(Main.worldName);
						writer.Write(Main.ActiveWorldFileData.UniqueId.ToByteArray());

						writer.BaseStream.Position += 49;
						writer.Write(build ? 0f : Main.maxRaining);
					}
					break;
				case 18:
					using (var writer = new BinaryWriter(new MemoryStream(e.Buffer, 3, e.Count - 3)))
					{
						writer.Write(build ? true : Main.dayTime);
						writer.Write(build ? 27000 : (int)Main.time);
					}
					break;
				case 23:
					NPC npc = Main.npc[BitConverter.ToInt16(e.Buffer, 3)];
					if (!npc.friendly)
					{
						Buffer.BlockCopy(BitConverter.GetBytes(build ? 0f : npc.position.X), 0, e.Buffer, 5, 4);
						Buffer.BlockCopy(BitConverter.GetBytes(build ? 0f : npc.position.Y), 0, e.Buffer, 9, 4);
					}
					break;
				case 27:
					short id = BitConverter.ToInt16(e.Buffer, 3);
					int owner = e.Buffer[27];
					Projectile proj = Main.projectile[TShock.Utils.SearchProjectile(id, owner)];
					if (!proj.friendly)
						Buffer.BlockCopy(BitConverter.GetBytes((short)(build ? 0 : proj.type)), 0, e.Buffer, 28, 2);
					break;
			}
		}

		void BuildModeCmd(CommandArgs e)
		{
			if (e.TPlayer.hostile)
			{
				e.Player.SendErrorMessage("You cannot enable Buildmode with PvP active!");
				return;
			}

			e.Player.SetData<bool>("buildmode", !e.Player.GetData<bool>("buildmode"));

			e.Player.SendSuccessMessage((e.Player.GetData<bool>("buildmode") ? "En" : "Dis") + "abled build mode.");
			// Time
			NetMessage.SendData(7, e.Player.Index);
			// NPCs
			for (int i = 0; i < 200; i++)
			{
				if (!Main.npc[i].friendly)
					e.Player.SendData(PacketTypes.NpcUpdate, "", i);
			}
			// Projectiles
			for (int i = 0; i < 1000; i++)
			{
				if (!Main.projectile[i].friendly)
					e.Player.SendData(PacketTypes.ProjectileNew, "", i);
			}
		}

		void BMCheck(CommandArgs args)
		{
			var plStr = String.Join(" ", args.Parameters);

			var ply = TShock.Utils.FindPlayer(plStr);
			if (ply.Count < 1)
			{
				args.Player.SendErrorMessage("No players matched that name!");
			}
			else if (ply.Count > 1)
			{
				TShock.Utils.SendMultipleMatchError(args.Player, ply.Select(p => p.Name));
			}
			else
			{
				if (ply[0].GetData<bool>("buildmode"))
				{
					args.Player.SendInfoMessage(ply[0].Name + " has Buildmode enabled!");
				}
				else
					args.Player.SendInfoMessage(ply[0].Name + " does not have Buildmode enabled.");
			}
		}
	}
}