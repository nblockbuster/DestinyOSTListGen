using WwiseParserLib.Structures.Chunks;
using WwiseParserLib.Structures.Objects.HIRC;
using WwiseParserLib.Structures.SoundBanks;
using System.Net;
using WwiseParserLib;
using Tiger;
using Tiger.Formats;
using Newtonsoft.Json;
using Serilog;

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
            string outputTemplate = "{Timestamp:HH:mm:ss} [{Level}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
            Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("list_gen.log", outputTemplate: outputTemplate)
            .Enrich.FromLogContext()
            .MinimumLevel.Verbose()
            .CreateLogger();
            Log.Information("Logger initialised");

            
            Extractor ext = null;


            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
            {
                Console.WriteLine("This program is used to generate a OSTs.db list of all GinsorIDs of music.");
                Console.WriteLine("Usage: DestinyOSTListGen.exe {path-to-bnk-or-pkgs-folder} [optional: --compare / -c]");
                Console.WriteLine("\t--compare (-c):\tcompares OSTs.db.old to a freshly generated list.");
                Console.WriteLine("\nxyx0826's WwiseParser is licensed under the MIT License.");
                return 0;
            }
            string bnk_or_pkgs_dir = args[0];
            List<string> GinsorIDs = new List<string>();
            List<double> Music_ms_Tracker = new List<double>();
            Dictionary<string, GinsorIdEntry> GinsorID_ref_dict = new Dictionary<string, GinsorIdEntry>();
            Dictionary<string, List<uint>> id_to_segment = new Dictionary<string, List<uint>>();
            Dictionary<string, List<string>> GinsorToBnk = new Dictionary<string, List<string>>();

            Log.Information("Starting...");

            List<Dictionary<uint, List<uint>>> packageid_to_refhash = new List<Dictionary<uint, List<uint>>>();


            if (Directory.GetFiles(bnk_or_pkgs_dir, "*.pkg").Length == 0)
            {
                foreach (string bnk in Directory.GetFiles(bnk_or_pkgs_dir))
                {
                    byte[] soundBankData = File.ReadAllBytes(bnk);
                    foreach (string gins in genList(soundBankData, ref Music_ms_Tracker, ref id_to_segment))
                    {
                        GinsorIDs.Add(gins);
                        if (!GinsorToBnk.ContainsKey(gins) || GinsorToBnk[gins] == null)
                        {
                            GinsorToBnk[gins] = new List<string>();
                        }
                        string name = bnk[(bnk.LastIndexOf('\\') + 1)..bnk.LastIndexOf('.')];
                        //name = name[..];
                        GinsorToBnk[gins].Add(name);
                    }
                }
            }
            else
            {
                ext = new Extractor(bnk_or_pkgs_dir, LoggerLevels.HighVerbouse);
                foreach (Package package in ext.master_packages_stream())
                {
                    if (!package.no_patch_id_name.Contains("audio"))
                    {
                        continue;
                    }
                    for (int entry_index = 0; entry_index < package.entry_table().Count; entry_index++)
                    {
                        Entry entry = package.entry_table()[entry_index];
                        if (entry.type == 26 && entry.subtype == 6)
                        {
                            byte[] bnkData = ext.extract_entry_data(package, entry).data;
                            foreach (string gins in genList(bnkData, ref Music_ms_Tracker, ref id_to_segment))
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
			/*
            foreach (string gins in UniqueGinsorIDs)
            {
                List<uint> SegmentIds = id_to_segment[gins].Distinct().ToList();
                uint idx = 0;
                //Package pkg = ext.find_pkg_of_ginsid(gins, ref idx);
                GinsorIdEntry ginsid_entry = new GinsorIdEntry();
                //ginsid_entry.reference = Utils.generate_reference_hash(pkg.package_id, idx);
                ginsid_entry.reference = null;
                ginsid_entry.SegmentIDs = SegmentIds;
                ginsid_entry.Soundbanks = GinsorToBnk[gins].Distinct().ToList();
                GinsorID_ref_dict[gins] = ginsid_entry;

            }

            string json = JsonConvert.SerializeObject(GinsorID_ref_dict, Formatting.Indented);

            File.WriteAllText("GinsorID_ref_dict.json", json);
            Log.Information("GinsorID_ref_dict.json written.");
            */
			
            File.WriteAllLines("OSTs.db", UniqueGinsorIDs);
            Log.Information($"Music Track Amount: {UniqueGinsorIDs.Count()}");
            Log.Information($"First Track Runtime: {Music_ms_Tracker[0]}");
            double overall_Ms = 0.0;
            foreach (double track_ms in Music_ms_Tracker)
            {
                overall_Ms += track_ms;
            }

            Log.Information($"Overall Runtime: {overall_Ms}");

            if (args.Contains("--compare") || args.Contains("-c"))
            {
                Log.Information("Comparing OSTs.db.old to OSTs.db.");

                if (!File.Exists("OSTs.db.old"))
                {
                    Log.Fatal("OSTs.db.old does not exist.");
                    return 0;
                }
                List<string> GinsorID_New = new List<string>();
                List<string> GinsorID_Removed = new List<string>();
                string[] Old_OSTs_Lines;
                Old_OSTs_Lines = File.ReadAllLines("OSTs.db.old");
                foreach (string new_ginsid in UniqueGinsorIDs)
                {
                    if (!Old_OSTs_Lines.Contains(new_ginsid))
                    {
                        Log.Information($"New GinsorID: {new_ginsid}");
                        GinsorID_New.Add(new_ginsid);
                    }

                }
                foreach (string old_ginsid in Old_OSTs_Lines)
                {
                    if (!UniqueGinsorIDs.Contains(old_ginsid))
                    {
                        Log.Information($"Old GinsorID {old_ginsid} does not exist in new list.");
                        GinsorID_Removed.Add(old_ginsid);
                    }
                }

                if (GinsorID_New.Count != 0 && GinsorID_Removed.Count != 0)
                {
                    foreach (string removed_ginsid in GinsorID_Removed)
                    {
                        Log.Information($"GinsorID {removed_ginsid} was removed.");
                    }
                    File.WriteAllLinesAsync("_Removed_GinsorIDs.txt", GinsorID_Removed);
                    foreach (string added_ginsid in GinsorID_New)
                    {
                        Log.Information($"GinsorID {added_ginsid} was added.");
                    }
                    File.WriteAllLinesAsync("_Added_GinsorIDs.txt", GinsorID_New);
                }
                else
                {
                    Log.Information("Nothing changed.");
                    return 0;
                }
            }

            return 0;
        }

        static List<string> genList(byte[] soundBankData, ref List<double> ms_Tracker, ref Dictionary<string, List<uint>> id_to_segment)
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
                                    var ginsid = ((uint)IPAddress.NetworkToHostOrder((int)sound.AudioId)).ToHex().ToUpper();
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
            return GinsorIDs;
        }
    }
}
