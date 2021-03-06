﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TsMap.HashFiles;

namespace TsMap
{
    public class TsMapper
    {
        private readonly string _gameDir;

        public RootFileSystem Rfs;

        private List<string> _sectorFiles;
        private readonly string[] _overlayFiles;

        private readonly Dictionary<ulong, TsPrefab> _prefabLookup = new Dictionary<ulong, TsPrefab>();
        private readonly Dictionary<ulong, TsCity> _citiesLookup = new Dictionary<ulong, TsCity>();
        private readonly Dictionary<ulong, TsRoadLook> _roadLookup = new Dictionary<ulong, TsRoadLook>();
        private readonly Dictionary<ulong, TsMapOverlay> _overlayLookup = new Dictionary<ulong, TsMapOverlay>();
        private readonly List<TsFerryConnection> _ferryConnectionLookup = new List<TsFerryConnection>();

        public readonly List<TsRoadItem> Roads = new List<TsRoadItem>();
        public readonly List<TsPrefabItem> Prefabs = new List<TsPrefabItem>();
        public readonly List<TsMapAreaItem> MapAreas = new List<TsMapAreaItem>();
        public readonly List<TsCityItem> Cities = new List<TsCityItem>();
        public readonly List<TsMapOverlayItem> MapOverlays = new List<TsMapOverlayItem>();
        public readonly List<TsFerryItem> FerryConnections = new List<TsFerryItem>();
        public readonly List<TsCompanyItem> Companies = new List<TsCompanyItem>();
        public readonly List<TsTriggerItem> Triggers = new List<TsTriggerItem>();

        public readonly Dictionary<ulong, TsNode> Nodes = new Dictionary<ulong, TsNode>();

        private List<TsSector> Sectors { get; set; }

        public TsMapper(string gameDir)
        {
            _gameDir = gameDir;
            Sectors = new List<TsSector>();
            
        }

        private void ParseCityFiles()
        {
            var defDirectory = Rfs.GetDirectory("def");
            if (defDirectory == null)
            {
                Log.Msg("Could not read 'def' dir");
                return;
            }

            var cityFiles = defDirectory.GetFiles("city");
            if (cityFiles == null)
            {
                Log.Msg("Could not read city files");
                return;
            }

            foreach (var cityFile in cityFiles)
            {
                var data = cityFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("@include"))
                    {
                        var path = Helper.GetFilePath(line.Split('"')[1], "def");
                        var city = new TsCity(this, path);
                        if (city.Token != 0 && !_citiesLookup.ContainsKey(city.Token))
                        {
                            _citiesLookup.Add(city.Token, city);
                        }
                    }
                }
            }
        }

        private void ParsePrefabFiles()
        {
            var worldDirectory = Rfs.GetDirectory("def/world");
            if (worldDirectory == null)
            {
                Log.Msg("Could not read 'def/world' dir");
                return;
            }

            var prefabFiles = worldDirectory.GetFiles("prefab");
            if (prefabFiles == null)
            {
                Log.Msg("Could not read prefab files");
                return;
            }

            foreach (var prefabFile in prefabFiles)
            {
                var data = prefabFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');

                var token = 0UL;
                var path = "";
                var category = "";

                foreach (var line in lines)
                {
                    if (line.Contains("prefab_model"))
                    {
                        token = ScsHash.StringToToken(line.Split('.')[1].Trim());
                    }
                    else if (line.Contains("prefab_desc"))
                    {
                        path = Helper.GetFilePath(line.Split('"')[1]);
                    }
                    else if (line.Contains("category"))
                    {
                        category = line.Split('"')[1];
                    }

                    if (line.Contains("}") && token != 0 && path != "")
                    {
                        var prefab = new TsPrefab(this, path, token, category);
                        if (prefab.Token != 0 && !_prefabLookup.ContainsKey(prefab.Token))
                        {
                            _prefabLookup.Add(prefab.Token, prefab);
                        }

                        token = 0;
                        path = "";
                        category = "";
                    }
                }
            }
        }

        private void ParseRoadLookFiles()
        {
            var worldDirectory = Rfs.GetDirectory("def/world");
            if (worldDirectory == null)
            {
                Log.Msg("Could not read 'def/world' dir");
                return;
            }

            var roadLookFiles = worldDirectory.GetFiles("road_look");
            if (roadLookFiles == null)
            {
                Log.Msg("Could not read road look files");
                return;
            }

            foreach (var roadLookFile in roadLookFiles)
            {
                var data = roadLookFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');
                TsRoadLook roadLook = null;

                foreach (var line in lines)
                {
                    if (line.Contains(":") && roadLook != null)
                    {
                        var value = line.Substring(line.IndexOf(':') + 1).Trim();
                        var key = line.Substring(0, line.IndexOf(':')).Trim();
                        switch (key)
                        {
                            case "lanes_left[]":
                                roadLook.LanesLeft.Add(value);
                                break;

                            case "lanes_right[]":
                                roadLook.LanesRight.Add(value);
                                break;
                            case "road_offset":
                                float.TryParse(value.Replace('.', ','), out roadLook.Offset);
                                break;
                        }
                    }

                    if (line.Contains("road_look"))
                    {
                        roadLook = new TsRoadLook(ScsHash.StringToToken(line.Split('.')[1].Trim('{').Trim()));
                    }

                    if (line.Contains("}") && roadLook != null)
                    {
                        if (roadLook.Token != 0 && !_roadLookup.ContainsKey(roadLook.Token))
                        {
                            _roadLookup.Add(roadLook.Token, roadLook);
                            roadLook = null;
                        }
                    }
                }
            }
        }

        private void ParseFerryConnections()
        {
            var connectionDirectory = Rfs.GetDirectory("def/ferry/connection");
            if (connectionDirectory == null)
            {
                Log.Msg("Could not read 'def/ferry/connection' dir");
                return;
            }

            var ferryConnectionFiles = connectionDirectory.GetFiles("sii");
            if (ferryConnectionFiles == null)
            {
                Log.Msg("Could not read ferry connection files files");
                return;
            }

            foreach (var ferryConnectionFile in ferryConnectionFiles)
            {
                var data = ferryConnectionFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');

                TsFerryConnection conn = null;

                foreach (var line in lines)
                {
                    if (line.Contains(":"))
                    {
                        var value = line.Split(':')[1].Trim();
                        var key = line.Split(':')[0].Trim();
                        if (conn != null)
                        {
                            if (key.Contains("connection_positions"))
                            {
                                var vector = value.Split('(')[1].Split(')')[0];
                                var values = vector.Split(',');
                                var x = float.Parse(values[0].Replace('.', ','));
                                var z = float.Parse(values[2].Replace('.', ','));
                                conn.AddConnectionPosition(x, z);
                            }
                        }

                        if (line.Contains("ferry_connection"))
                        {
                            var portIds = value.Split('.');
                            conn = new TsFerryConnection
                            {
                                StartPortToken = ScsHash.StringToToken(portIds[1]),
                                EndPortToken = ScsHash.StringToToken(portIds[2].TrimEnd('{').Trim())
                            };
                        }
                    }

                    if (!line.Contains("}") || conn == null) continue;;

                    var existingItem = _ferryConnectionLookup.FirstOrDefault(item =>
                        (item.StartPortToken == conn.StartPortToken && item.EndPortToken == conn.EndPortToken) ||
                        (item.StartPortToken == conn.EndPortToken && item.EndPortToken == conn.StartPortToken)); // Check if connection already exists
                    if (existingItem == null) _ferryConnectionLookup.Add(conn);
                    conn = null;
                }
            }
        }

        private void ParseOverlays()
        {
            var uiMapDirectory = Rfs.GetDirectory("material/ui/map");
            if (uiMapDirectory == null)
            {
                Log.Msg("Could not read 'material/ui/map' dir");
                return;
            }

            var matFiles = uiMapDirectory.GetFiles(".mat");
            if (matFiles == null)
            {
                Log.Msg("Could not read .mat files");
                return;
            }

            var uiMapRoadDirectory = Rfs.GetDirectory("material/ui/map/road");
            if (uiMapRoadDirectory != null)
            {
                var data = uiMapRoadDirectory.GetFiles(".mat");
                if (data != null) matFiles.AddRange(data);
            }
            else
            {
                Log.Msg("Could not read 'material/ui/map/road' dir");
            }

            var uiCompanyDirectory = Rfs.GetDirectory("material/ui/company/small");
            if (uiCompanyDirectory != null)
            {
                var data = uiCompanyDirectory.GetFiles(".mat");
                if (data != null) matFiles.AddRange(data);
            }
            else
            {
                Log.Msg("Could not read 'material/ui/company/small' dir");
            }

            foreach (var matFile in matFiles)
            {
                var data = matFile.Entry.Read();
                var lines = Encoding.UTF8.GetString(data).Split('\n');

                foreach (var line in lines)
                {
                    if (line.Contains("texture") && !line.Contains("_name"))
                    {
                        var tobjPath = Helper.CombinePath(matFile.GetLocalPath(), line.Split('"')[1]);

                        var tobjData = Rfs.GetFileEntry(tobjPath).Entry.Read();

                        var pathLength = MemoryHelper.ReadInt32(tobjData, 0x28);
                        var path = Helper.GetFilePath(Encoding.UTF8.GetString(tobjData, 0x30, pathLength));

                        var name = matFile.GetFileName();
                        if (name.StartsWith("map")) continue;
                        if (name.StartsWith("road_")) name = name.Substring(5);

                        var token = ScsHash.StringToToken(name);
                        if (!_overlayLookup.ContainsKey(token))
                        {
                            _overlayLookup.Add(token, new TsMapOverlay(this, path));
                        }

                    }
                }
            }
        }

        /// <summary>
        /// Parse all definition files
        /// </summary>
        private void ParseDefFiles()
        {
            ParseCityFiles();
            ParsePrefabFiles();
            ParseRoadLookFiles();
            ParseFerryConnections();
            ParseOverlays();
        }

        /// <summary>
        /// Parse all .base files
        /// </summary>
        private void ParseMapFiles()
        {
            var baseMapEntry = Rfs.GetDirectory("map");
            if (baseMapEntry == null)
            {
                Log.Msg("Could not read 'map' dir");
                return;
            }

            var mbd = baseMapEntry.Files.Values.FirstOrDefault(x => x.GetExtension().Equals("mbd")); // Get the map name from the mbd file
            if (mbd == null)
            {
                Log.Msg("Could not find mbd file");
                return;
            }

            var mapName = mbd.GetFileName();

            var mapFileDir = Rfs.GetDirectory($"map/{mapName}");
            if (mapFileDir == null)
            {
                Log.Msg($"Could not read 'map/{mapName}' directory");
                return;
            }

            _sectorFiles = mapFileDir.GetFiles(".base").Select(x => x.GetPath()).ToList();
        }

        /// <summary>
        /// Parse through all .scs files and retreive all necessary files
        /// </summary>
        public void Parse()
        {
            var startTime = DateTime.Now.Ticks;

            if (!Directory.Exists(_gameDir))
            {
                Log.Msg("Could not find Game directory.");
                return;
            }

            try
            {
                Rfs = new RootFileSystem(_gameDir);
            }
            catch (FileNotFoundException e)
            {
                Log.Msg(e.Message);
                return;
            }
            

            ParseDefFiles();
            ParseMapFiles();

            
            if (_sectorFiles == null) return;

            var preMapParseTime = DateTime.Now.Ticks;
            Sectors = _sectorFiles.Select(file => new TsSector(this, file)).ToList();
            Sectors.ForEach(sec => sec.Parse());
            Sectors.ForEach(sec => sec.ClearFileData());
            Log.Msg($"It took {(DateTime.Now.Ticks - preMapParseTime) / TimeSpan.TicksPerMillisecond} ms to parse all (*.base)" +
                    $" map files and {(DateTime.Now.Ticks - startTime) / TimeSpan.TicksPerMillisecond} ms total.");
        }

        public TsNode GetNodeByUid(ulong uid)
        {
            return Nodes.ContainsKey(uid) ? Nodes[uid] : null;
        }

        public TsRoadLook LookupRoadLook(ulong lookId)
        {
            return _roadLookup.ContainsKey(lookId) ? _roadLookup[lookId] : null;
        }

        public TsPrefab LookupPrefab(ulong prefabId)
        {
            return _prefabLookup.ContainsKey(prefabId) ? _prefabLookup[prefabId] : null;
        }

        public TsCity LookupCity(ulong cityId)
        {
            return _citiesLookup.ContainsKey(cityId) ? _citiesLookup[cityId] : null;
        }

        public TsMapOverlay LookupOverlay(ulong overlayId)
        {
            return _overlayLookup.ContainsKey(overlayId) ? _overlayLookup[overlayId] : null;
        }

        public List<TsFerryConnection> LookupFerryConnection(ulong ferryPortId)
        {
            return _ferryConnectionLookup.Where(item => item.StartPortToken == ferryPortId).ToList();
        }

        public void AddFerryPortLocation(ulong ferryPortId, float x, float z)
        {
            var ferry = _ferryConnectionLookup.Where(item => item.StartPortToken == ferryPortId || item.EndPortToken == ferryPortId);
            foreach (var connection in ferry)
            {
                connection.SetPortLocation(ferryPortId, x, z);
            }
        }
    }
}
