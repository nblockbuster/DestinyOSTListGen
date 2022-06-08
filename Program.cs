using WwiseParserLib.Structures.Chunks;
using WwiseParserLib.Structures.Objects.HIRC;
using WwiseParserLib.Structures.SoundBanks;
using System.Net;
using WwiseParserLib;


namespace DestinyOSTListGen
{
    class Program
    {
        static async Task<int> Main(string [] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
            {
                Console.WriteLine("This program is used to generate a OSTs.db list of all GinsorIDs of music.");
                Console.WriteLine("Usage: DestinyOSTListGen.exe {path-to-bnk-folder} [optional: --compare / -c]");
                Console.WriteLine("\t--compare (-c):\tcompares OSTs.db.old to a freshly generated list.");
                Console.WriteLine("\nxyx0826's WwiseParser is licensed under the MIT License.");
                return 0;
            }

            string bnk_dir = args[0];

            List<string> GinsorIDs = new List<string>();
            foreach (string bnk in Directory.GetFiles(bnk_dir))
            {
                byte[] soundBankData = File.ReadAllBytes(bnk);
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
                                    var track = srch_obj as MusicTrack;
                                    for (int x = 0; x < track.SoundCount; x++)
                                    {
                                        var sound = track.Sounds[x];
                                        var ginsid = ((uint)IPAddress.NetworkToHostOrder((int)sound.AudioId)).ToHex().ToUpper();
                                        Console.WriteLine($"GinsorID of track {track.Id} (Parent Segment: {segment.Id}): {ginsid}");
                                        GinsorIDs.Add(ginsid);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            if (GinsorIDs.Count == 0)
            {
                throw new Exception("GinsorID table empty?");
            }
            var UniqueGinsorIDs = GinsorIDs.Distinct();
            await File.WriteAllLinesAsync("OSTs.db", UniqueGinsorIDs);
            Console.WriteLine($"Music Track Amount: {UniqueGinsorIDs.Count()}");

            

            if (args.Contains("--compare") || args.Contains("-c"))
            {
                Console.WriteLine("Comparing OSTs.db.old to OSTs.db.");

                if (!File.Exists("OSTs.db.old"))
                {
                    Console.WriteLine("Old_OSTs.db does not exist.");
                    return 0;
                }
                List<string> GinsorID_New = new List<string>();
                List<string> GinsorID_Removed = new List<string>();
                string[] Old_OSTs_Lines;
                Old_OSTs_Lines = await File.ReadAllLinesAsync("OSTs.db.old");
                foreach (string new_ginsid in UniqueGinsorIDs)
                {
                    if (!Old_OSTs_Lines.Contains(new_ginsid))
                    {
                        //Console.WriteLine($"New GinsorID: {new_ginsid}");
                        GinsorID_New.Add(new_ginsid);
                    }
                    
                }
                foreach (string old_ginsid in Old_OSTs_Lines)
                {
                    if (!UniqueGinsorIDs.Contains(old_ginsid))
                    {
                        //Console.WriteLine($"Old GinsorID {old_ginsid} does not exist in new list.");
                        GinsorID_Removed.Add(old_ginsid);
                    }
                }

                foreach (string removed_ginsid in GinsorID_Removed)
                {
                    Console.WriteLine($"GinsorID {removed_ginsid} does not exist in new list.");
                }
                await File.WriteAllLinesAsync("Removed_GinsorIDs.txt", GinsorID_Removed);
                foreach (string added_ginsid in GinsorID_New)
                {
                    Console.WriteLine($"GinsorID {added_ginsid} was added.");
                }
                await File.WriteAllLinesAsync("Added_GinsorIDs.txt", GinsorID_New);
            }

            return 0;
        }
    }
}
