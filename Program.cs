using WwiseParserLib.Structures.Chunks;
using WwiseParserLib.Structures.Objects.HIRC;
using WwiseParserLib.Structures.SoundBanks;
using System.Net;
using WwiseParserLib;
using WwiseParserLib.Structures.Hierarchies;
using Tiger;
using Tiger.Formats;
using Newtonsoft.Json;
using Serilog;
using System.Collections.Concurrent;

namespace DestinyOSTListGen
{
    class Program
    {
        struct GinsorIdEntry
        {
            public Utils.EntryReference reference;
            public List<uint> SegmentIDs;
            public List<string> Soundbanks;
        }

        static int Main(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
            {
                Console.WriteLine("This program is used to generate a OSTs.db list of all GinsorIDs of music.");
                Console.WriteLine("Usage: DestinyOSTListGen.exe {path-to-bnk-or-pkgs-folder} [optional: --compare / -c]");
                Console.WriteLine("\t--compare (-c):\tcompares OSTs.db.old to a freshly generated list.");
                Console.WriteLine("\nxyx0826's WwiseParser is licensed under the MIT License.");
                return 0;
            }

            bool sfx = false;

            File.WriteAllText("list_gen.log", string.Empty);
            
            string outputTemplate = "{Timestamp:HH:mm:ss} [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("list_gen.log", outputTemplate: outputTemplate)
            .Enrich.FromLogContext()
            .MinimumLevel.Verbose()
            .CreateLogger();
            Log.Information("Logger initialised");


            Extractor? ext = null;

            List<string> MusicIds = new List<string>();

            if (args.Contains("--sfx") || args.Contains("-s"))
            {
                sfx = true;
                MusicIds = File.ReadAllLines("OSTs.db").ToList();
            }

            string bnk_or_pkgs_dir = args[0];
            List<string> GinsorIDs = new List<string>();
            List<double> Music_ms_Tracker = new List<double>();
            ConcurrentDictionary<string, GinsorIdEntry> GinsorID_ref_dict = new ConcurrentDictionary<string, GinsorIdEntry>();
            ConcurrentDictionary<string, List<uint>> id_to_segment = new ConcurrentDictionary<string, List<uint>>();
            ConcurrentDictionary<string, List<string>> GinsorToBnk = new ConcurrentDictionary<string, List<string>>();

            Log.Information("Starting...");

            List<Dictionary<uint, List<uint>>> packageid_to_refhash = new List<Dictionary<uint, List<uint>>>();


            if (Directory.GetFiles(bnk_or_pkgs_dir, "*.pkg").Length == 0)
            {
                _ = Parallel.ForEach(Directory.GetFiles(bnk_or_pkgs_dir), bnk =>
                {
                    byte[] soundBankData = File.ReadAllBytes(bnk);
                    foreach (string gins in genList(soundBankData, ref Music_ms_Tracker, ref id_to_segment, sfx, MusicIds))
                    {
                        GinsorIDs.Add(gins);
                        if (!GinsorToBnk.ContainsKey(gins) || GinsorToBnk[gins] == null)
                        {
                            GinsorToBnk[gins] = new List<string>();
                        }
                        string name = bnk[(bnk.LastIndexOf('\\') + 1)..bnk.LastIndexOf('.')];
                        GinsorToBnk[gins].Add(name);
                    }
                });
            }
            else
            {
                ext = new Extractor(bnk_or_pkgs_dir, LoggerLevels.HighVerbouse);
                var st = ext.master_packages_stream();
                foreach (Package package in st)
                {
                    if (!package.no_patch_id_name.Contains("audio") && !package.no_patch_id_name.Contains("_en_"))
                    {
                        continue;
                    }
                    foreach (var entry in package.entry_table())
                    {
                        if (entry.type == 26 && entry.subtype == 6)
                        {
                            byte[] bnkData = ext.extract_entry_data(package, entry).data;
                            foreach (string gins in genList(bnkData, ref Music_ms_Tracker, ref id_to_segment, sfx, MusicIds))
                            {
                                GinsorIDs.Add(gins);
                                if (!GinsorToBnk.ContainsKey(gins) || GinsorToBnk[gins] == null)
                                {
                                    GinsorToBnk[gins] = new List<string>();
                                }
                                GinsorToBnk[gins].Add(Utils.entry_name(package, entry));
                            }
                        }
                    }
                }
            }
            GC.Collect();
            if (GinsorIDs.Count == 0)
            {
                throw new Exception("GinsorID table empty?");
            }
            var UniqueGinsorIDs = GinsorIDs.Distinct().ToList();
            Log.Information($"Raw GinsorID List Count: {GinsorIDs.Count} || Unique GinsorID List Count: {UniqueGinsorIDs.Count}");

            //used in DMV

            //_ = Parallel.ForEach(UniqueGinsorIDs, gins =>
            //{
            //    List<uint> SegmentIds = id_to_segment[gins].Distinct().ToList();
            //    uint idx = 0;
            //    Package? pkg = Task.Run(() => ext.find_pkg_of_ginsid(gins, ref idx)).Result;

            //    GinsorIdEntry ginsid_entry = new GinsorIdEntry
            //    {
            //        reference = Utils.generate_reference_hash(pkg.package_id, idx),
            //        SegmentIDs = SegmentIds,
            //        Soundbanks = GinsorToBnk[gins].Distinct().ToList()
            //    };
            //    //ginsid_entry.reference = Utils.generate_reference_hash(pkg.package_id, idx);
            //    //ginsid_entry.reference = null;
            //    //ginsid_entry.SegmentIDs = SegmentIds;
            //    //ginsid_entry.Soundbanks = GinsorToBnk[gins].Distinct().ToList();
            //    GinsorID_ref_dict[gins] = ginsid_entry;
            //});

            string json = JsonConvert.SerializeObject(GinsorID_ref_dict, Formatting.Indented);

            File.WriteAllText("GinsorID_ref_dict.json", json);
            Log.Information("GinsorID_ref_dict.json written.");

            //Log.Information($"Unique Amount: {UniqueGinsorIDs.Count()}");

            if (!sfx)
            {
                File.WriteAllLines("OSTs.db", UniqueGinsorIDs);
                Log.Information($"First Track Runtime: {Music_ms_Tracker[0]}");
                double overall_Ms = 0.0;
                Parallel.ForEach(Music_ms_Tracker, track_ms =>
                {
                    overall_Ms += track_ms;
                });

                Log.Information($"Overall Runtime: {overall_Ms}");
            }
            else
                File.WriteAllLines("SFX.db", UniqueGinsorIDs);


            if (args.Contains("--compare") || args.Contains("-c"))
            {
                string file_name_base = "Music GinsorID";
                if (sfx)
                        file_name_base = "SFX GinsorID";
                
                string file_output_name = string.Join('_', file_name_base.Split(' '));
                
                Log.Information("Comparing OSTs.db.old to OSTs.db.");
                if (!sfx && !File.Exists("OSTs.db.old"))
                {
                    Log.Fatal("OSTs.db.old does not exist.");
                    return 0;
                }
                else if (sfx && !File.Exists("SFX.db.old"))
                {
                    Log.Fatal("SFX.db.old does not exist.");
                    return 0;
                }
                List<string> GinsorID_New = new List<string>();
                List<string> GinsorID_Removed = new List<string>();
                string[] Old_OSTs_Lines;

                if (!sfx)
                    Old_OSTs_Lines = File.ReadAllLines("OSTs.db.old");
                else
                    Old_OSTs_Lines = File.ReadAllLines("SFX.db.old");

                Parallel.ForEach(UniqueGinsorIDs, new_ginsid =>
                {
                    if (!Old_OSTs_Lines.Contains(new_ginsid))
                    {
                        Log.Information($"New {file_name_base}: {new_ginsid}");
                        GinsorID_New.Add(new_ginsid);
                    }

                });
                Parallel.ForEach(Old_OSTs_Lines, old_ginsid =>
                {
                    if (!UniqueGinsorIDs.Contains(old_ginsid))
                    {
                        Log.Information($"Old {file_name_base} {old_ginsid} does not exist in new list.");
                        GinsorID_Removed.Add(old_ginsid);
                    }
                });
                if (GinsorID_New.Count != 0)
                {
                    Parallel.ForEach(GinsorID_New, added_ginsid =>
                    {
                        Log.Information($"{file_name_base} {added_ginsid} was added.");
                    });
                    _ = File.WriteAllLinesAsync($"_Added_{file_output_name}.txt", GinsorID_New);
                }
                else
                {
                    Log.Information("Nothing added.");
                }
                if (GinsorID_Removed.Count != 0)
                {
                    Parallel.ForEach(GinsorID_Removed, removed_ginsid =>
                    {
                        Log.Information($"GinsorID {removed_ginsid} was removed.");
                    });
                    File.WriteAllLinesAsync($"_Removed_{file_output_name}.txt", GinsorID_Removed);
                }
                else
                {
                    Log.Information("Nothing removed.");
                }
                var diff = GinsorID_New.Count() - GinsorID_Removed.Count();
                var stringdiff = "";

                if (diff < 0)
                {
                    stringdiff = $"{diff}";
                }
                else if (diff == 0)
                {
                    stringdiff = $"No Change";
                }
                else
                {
                    stringdiff = $"+{diff}";
                }

                Log.Information($"Difference: {stringdiff}");
                Log.Information($"Added: {GinsorID_New.Count()}  |  Removed: {GinsorID_Removed.Count()}");
            }

            return 0;
        }

        static List<string> genList(byte[] soundBankData, ref List<double> ms_Tracker,
            ref ConcurrentDictionary<string, List<uint>> id_to_segment, bool sfx, List<string> MusicIds)
        {
            List<string> GinsorIDs = new List<string>();
            SoundBank memSoundBank = new InMemorySoundBank(soundBankData);
            var bkhd = memSoundBank.ParseChunk(SoundBankChunkType.BKHD);
            if (bkhd == null)
            {
                throw new Exception("The specified file does not have a valid SoundBank header.");
            }
            var hirc = memSoundBank.GetChunk(SoundBankChunkType.HIRC);
            if (hirc == null)
            {
                throw new Exception("The specified file does not have a valid Hierarchy header.");
            }

            var musicObjs = (hirc as SoundBankHierarchyChunk).Objects
                .Where(o => o is MusicObject)
                .Select(o => o as MusicObject);

            var sfxObjs = (hirc as SoundBankHierarchyChunk).Objects
                .Where(o => o is SoundObject)
                .Select(o => o as SoundObject);

            if (!sfx)
            {
                foreach (var obj in musicObjs)
                {
                    if (obj.Type == HIRCObjectType.MusicSegment)
                    {
                        var segment = obj as MusicSegment;
                        for (int i = 0; i < segment.ChildCount; i++)
                        {
                            foreach (var srch_obj in musicObjs)
                            {
                                if (srch_obj.Id == segment.ChildIds[i])
                                {
                                    bool alreadyInList = false;
                                    var track = srch_obj as MusicTrack;
                                    for (int x = 0; x < track.SoundCount; x++)
                                    {
                                        var sound = track.Sounds[x];
                                        var ginsid = ((uint)IPAddress.NetworkToHostOrder((int)sound.AudioId)).ToString("X8");
                                        if (GinsorIDs.Contains(ginsid))
                                        {
                                            alreadyInList = true;
                                            continue;
                                        }
                                        //Logger.log($"GinsorID of track {track.Id} (Parent Segment: {segment.Id}): {ginsid}", LoggerLevels.HighVerbouse);
                                        if (!id_to_segment.ContainsKey(ginsid) || id_to_segment[ginsid] == null)
                                        {
                                            id_to_segment[ginsid] = new List<uint>();
                                        }
                                        id_to_segment[ginsid].Add(segment.Id);

                                        GinsorIDs.Add(ginsid);
                                    }
                                    if (!alreadyInList)
                                    {
                                        for (int b = 0; b < track.TimeParameterCount; b++)
                                        {
                                            var TimeParam = track.TimeParameters[b];
                                            var EndOffset = TimeParam.EndOffset;
                                            ms_Tracker.Add(EndOffset);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                foreach (var obj in sfxObjs)
                {
                    if (obj.Type == HIRCObjectType.Sound)
                    {
                        var sound = obj as Sound;
                        bool alreadyInList = false;
                        var ginsid = ((uint)IPAddress.NetworkToHostOrder((int)sound.AudioId)).ToString("X8");
                        if (GinsorIDs.Contains(ginsid))
                        {
                            continue;
                        }
                        else if (MusicIds.Contains(ginsid))
                        {
                            continue;
                        }
                        if (!id_to_segment.ContainsKey(ginsid) || id_to_segment[ginsid] == null)
                        {
                            id_to_segment[ginsid] = new List<uint>();
                        }
                        id_to_segment[ginsid].Add(sound.Id);

                        GinsorIDs.Add(ginsid);
                    }
                }
            }
            return GinsorIDs;
        }
    }
}
