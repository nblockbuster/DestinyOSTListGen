using WwiseParserLib.Structures.Chunks;
using WwiseParserLib.Structures.Objects.HIRC;
using WwiseParserLib.Structures.SoundBanks;
using System.Net;
using WwiseParserLib;


namespace gen_music_list
{
    class Program
    {
        static async Task<int> Main(string [] args)
        {
            List<string> GinsorIDs = new List<string>();
            string bnk_dir = "E:\\DestinyMusic\\TWQBnks";
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
                List<string> GinsorID_Diff = new List<string>();
                string[] Old_OSTs_Lines;
                Old_OSTs_Lines = await File.ReadAllLinesAsync("OSTs.db.old");
                foreach (string new_ginsid in UniqueGinsorIDs)
                {
                    if (!Old_OSTs_Lines.Contains(new_ginsid))
                    {
                        Console.WriteLine($"New GinsorID: {new_ginsid}");
                        GinsorID_Diff.Add(new_ginsid);
                    }
                }
            }

            return 0;
        }
    }
}
