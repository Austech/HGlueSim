﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lidgren.Network;
using SFML.Window;
using SettlersEngine;
using Shared;
using Server.Entities;
using Server.Level;

namespace Server.GameModes
{
    class StandardMelee : GameModeBase
    {
        public enum StatusState : byte
        {
            InProgress,
            WaitingForPlayers,
            Completed,
        }


        private TileMap map;

        public byte MaxPlayers;
        public byte idToGive;

        private SettlersEngine.SpatialAStar<PathNode, object> pathFinding; 

        private StatusState _gameStatus;
        public StatusState GameStatus
        {
            get { return _gameStatus; }

            set
            {
                _gameStatus = value;
                SendData(new byte[1] {(byte) _gameStatus}, Gamemode.Signature.Custom);
            }
        }


        public BuildingBase Building;
        
        public StandardMelee(GameServer server, byte mplayers) : base(server)
        {
            MaxPlayers = mplayers;
            GameStatus = StatusState.WaitingForPlayers;
            idToGive = 0;

            map = new TileMap();
            map.SetMap<TileBase>(50, 50);


            var resources = new Resources(server);
            resources.Position = new Vector2f(400, 200);
            resources.ResourceType = ResourceTypes.Apple;
            AddEntity(resources);


            pathFinding = new SpatialAStar<PathNode, object>(map.GetPathNodeMap());
        }

        public override void Update(float ms)
        {
            base.Update(ms);
            switch(GameStatus)
            {
                case StatusState.InProgress:
                    {
                        //Check if players are still playing

                        //TODO: Change back to non-comment when ready
                        //base.Update(ms);
                        var team1Count = 0;
                        var team1Id = 0;
                        var gameInProgress = false;
                        foreach(var player in players)
                        {
                            if(player.Status == Player.StatusTypes.InGame)
                            {
                                if(team1Count == 0)
                                {
                                    team1Id = player.Team;
                                    team1Count++;
                                }
                                else
                                {
                                    if(team1Id != player.Team)
                                    {
                                        gameInProgress = true;
                                        break;
                                    }
                                }
                            }
                        }
                        //If there's not enough players playing, the game is completed
                        if(gameInProgress == false)
                        {
                            GameStatus = StatusState.Completed;
                        }

                        //Check if player has lost all their buildings

                        foreach(var player in players)
                        {
                            var team = player.Team;
                            var hasBuilding = false;
                            foreach (var entity in WorldEntities.Values)
                            {
                                if(entity.Team == team && entity is BuildingBase)
                                {
                                    hasBuilding = true;
                                    break;
                                }
                            }
                            if(hasBuilding == false)
                            {
                               //player has been eliminated
                            }
                        }
                    }

                    break;
                case StatusState.WaitingForPlayers:
                    break;
                case StatusState.Completed:
                    //TODO: Change back to non-comment when ready
                    //base.Update(ms);
                    break;
                default:
                    break;
            }

            
        }

        public override PathFindReturn PathFindNodes(float sx, float sy, float x, float y)
        {
            var path =
                pathFinding.Search(
                    new Point((int)sx / map.TileSize.X,
                              (int)sy / map.TileSize.Y),
                    new Point((int)x / map.TileSize.X,
                              (int)y / map.TileSize.Y), null);

            
            return new PathFindReturn()
                       {
                           List = path,
                           MapSize = map.TileSize,
                       };
        }

        public override void UpdatePlayer(Player player)
        {
            var memory = new MemoryStream();
            var writer = new BinaryWriter(memory);

            writer.Write((byte) player.ClientId);
            writer.Write(player.ToBytes());
            SendData(memory.ToArray(), Gamemode.Signature.PlayerData);

            memory.Close();
            writer.Close();
        }

        public override void OnStatusChange(NetConnection connection, NetConnectionStatus status)
        {
            switch(status)
            {
                case NetConnectionStatus.None:
                    break;
                case NetConnectionStatus.InitiatedConnect:
                    break;
             case NetConnectionStatus.RespondedAwaitingApproval:
                    break;
                case NetConnectionStatus.RespondedConnect:
                    break;
                case NetConnectionStatus.Connected:
                    {
                        if (players.Count < MaxPlayers)
                        {
                            //Connected client must be a player

                            var nPlayer = new Player {ClientId = idToGive, Team = idToGive};
                            nPlayer.Apples = 50;
                            nPlayer.Supply = 10;
                            players.Add(nPlayer);
                            connection.Tag = nPlayer;

                            HomeBuilding home = new HomeBuilding(Server, nPlayer);
                            home.Team = nPlayer.Team;
                            home.Position = new Vector2f(100*players.Count, 500);
                            AddEntity(home);

                            SendAllPlayers();
                            SendData(map.ToBytes(), Gamemode.Signature.MapLoad);
                            SendAllEntities();

                            if (players.Count >= MaxPlayers)
                            {
                                GameStatus = StatusState.InProgress;
                            }
                        }
                        else
                        {
                            //Connectd client must be a spectator or something non-player type?
                            SendAllPlayers();
                            SendData(map.ToBytes(), Gamemode.Signature.MapLoad);
                            SendAllEntities();
                        }
                        //we don't increase the idToGive here, that's handled by the handshake.
                    }
                    break;
                case NetConnectionStatus.Disconnecting:

                    for(int i = 0; i < players.Count; i++)
                    {
                        if(players[i] == connection.Tag)
                        {
                            var memory = new MemoryStream();
                            var writer = new BinaryWriter(memory);

                            players[i].Status = Player.StatusTypes.Left;

                            writer.Write((byte) Gamemode.Signature.PlayerLeft);
                            writer.Write(players[i].ClientId);

                            SendData(memory.ToArray(), Gamemode.Signature.Custom);

                            writer.Close();
                            memory.Close();
                            
                            players.RemoveAt(i);
                            break;
                        }
                    }

                    break;
                case NetConnectionStatus.Disconnected:
                    break;
                default:
                    break;
            }
        }

        public override void ParseInput(MemoryStream memory, NetConnection client)
        {
            var reader = new BinaryReader(memory);

            var type = (InputSignature)reader.ReadByte();

            switch (type)
            {
                case InputSignature.Movement:
                    {
                        var player = (Player) client.Tag;

                        var posX = reader.ReadSingle();
                        var posY = reader.ReadSingle();
                        var reset = reader.ReadBoolean();
                        var attackMove = reader.ReadBoolean();
                        var unitCount = reader.ReadByte();

                        for (var i = 0; i < unitCount; i++)
                        {
                            var entityId = reader.ReadUInt16();
                            if (entities.ContainsKey(entityId) == false) continue;
                            if (entities[entityId].Team != player.Team) continue;

                            if (!attackMove)
                            {
                                entities[entityId].Move(posX, posY, Entity.RallyPoint.RallyTypes.StandardMove, reset);
                            }
                            else
                            {
                                entities[entityId].Move(posX, posY, Entity.RallyPoint.RallyTypes.AttackMove, reset);
                            }


                            entities[entityId].OnPlayerCustomMove();

                            if (entities[entityId] is UnitBase)
                            {
                                var unitCast = (UnitBase) entities[entityId];

                                if (attackMove)
                                {
                                    unitCast.State = UnitBase.UnitState.Agro;
                                }
                                else
                                {
                                    unitCast.State = UnitBase.UnitState.Standard;
                                }
                            }
                        }
                    }
                    break;
                case InputSignature.CreateUnit:
                    {
                        var unitToCreate = reader.ReadByte();
                        var unitCount = reader.ReadByte();
                        var player = (Player) client.Tag;

                        BuildingBase buildingToUse = null;

                        /*
                        /// We want to find the building that has the least producing units to make production faster and easier
                        /// For example, if the client has 2 buildings, and want to produce a zealot
                        /// If building 1 has a zealot in production, but building 2 is not in use, it'll choose building 2 to producte
                        */

                        for (var i = 0; i < unitCount; i++)
                        {
                            var entityId = reader.ReadUInt16();
                            if (entities.ContainsKey(entityId) == false) continue;
                            if (entities[entityId].Team != player.Team) continue;

                            var entity = entities[entityId];

                            if (entity is BuildingBase)
                            {
                                var building = (BuildingBase) entity;
                                if (buildingToUse == null || building.BuildOrderCount < buildingToUse.BuildOrderCount)
                                {
                                    buildingToUse = building;
                                }
                            }


                        }

                        if (buildingToUse != null)
                        {
                            buildingToUse.StartProduce(unitToCreate);
                        }
                    }
                    break;
                case InputSignature.ChangeUseEntity:
                    {
                        var player = (Player) client.Tag;
                        var useEntity = reader.ReadUInt16();

                        var unitCount = reader.ReadByte();
                        for (var i = 0; i < unitCount; i++)
                        {
                            var entityId = reader.ReadUInt16();
                            if (entities.ContainsKey(entityId) == false || entities.ContainsKey(useEntity) == false)
                                continue;
                            if (entities[entityId].Team != player.Team) continue;

                            entities[entityId].SetEntityToUse(entities[useEntity]);
                        }
                    }
                    break;
                case InputSignature.SpellCast:
                    {
                        var player = (Player) client.Tag;
                        var spell = reader.ReadByte();
                        var x = reader.ReadSingle();
                        var y = reader.ReadSingle();
                        var unitCount = reader.ReadByte();
                        for (var i = 0; i < unitCount; i++)
                        {
                            var entityId = reader.ReadUInt16();
                            if (entities.ContainsKey(entityId) == false) continue;
                            if (entities[entityId].Team != player.Team) continue;

                            if (entities[entityId].CastSpell(spell, x, y))
                                break;
                            //We break because we only want one unit to cast this spell
                        }

                    }
                    break;

                case InputSignature.Surrender:
                    {
                        var outmemory = new MemoryStream();
                        var writer = new BinaryWriter(outmemory);

                        var player = (Player)client.Tag;
                        player.Status = Player.StatusTypes.Left;

                        writer.Write((byte) StandardMeleeSignature.PlayerSurrender);
                        writer.Write(player.ClientId);
                        SendData(outmemory.ToArray(), Gamemode.Signature.Custom);

                        outmemory.Close();
                        writer.Close();
                    }
                    break;
                default:
                    break;
            }

        }

        public override byte[] HandShake()
        {
            var memory = new MemoryStream();
            var writer = new BinaryWriter(memory);

            writer.Write(idToGive);
            idToGive++;

            return memory.ToArray();
        }

    }
}
