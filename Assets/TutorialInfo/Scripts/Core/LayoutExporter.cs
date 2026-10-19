using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LightPCG.Core
{
    /// <summary>
    /// Writes one JSON file per generated level so that every feature can be
    /// recomputed later without re-running Unity (reproducibility).
    /// </summary>
    public static class LayoutExporter
    {
        public static void Write(string folder, int run, int seed, GridModel board,
                                 Vector2Int emitter, Vector2Int emitDir, Vector2Int receiver,
                                 IntendedSolution sol, ScrambleResult scr)
        {
            Directory.CreateDirectory(folder);
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"run\": {run}, \"seed\": {seed}, \"width\": {board.Width}, \"height\": {board.Height},\n");
            sb.Append($"  \"emitter\": [{emitter.x},{emitter.y}], \"emitDir\": [{emitDir.x},{emitDir.y}],\n");
            sb.Append($"  \"receiver\": [{receiver.x},{receiver.y}],\n");

            var walls = new List<string>(); var doors = new List<string>();
            for (int x = 0; x < board.Width; x++)
                for (int y = 0; y < board.Height; y++)
                {
                    var t = board.GetTile(x, y);
                    if (t == TileType.Wall) walls.Add($"[{x},{y}]");
                    else if (t == TileType.Door) doors.Add($"[{x},{y}]");
                }
            sb.Append($"  \"walls\": [{string.Join(",", walls)}],\n");
            sb.Append($"  \"doors\": [{string.Join(",", doors)}],\n");

            sb.Append($"  \"movementRule\": \"{MovementRule.Describe()}\",\n");
            sb.Append("  \"solved\": ").Append(Pieces(sol.Solved)).Append(",\n");
            sb.Append("  \"initial\": ").Append(Pieces(scr.Initial)).Append(",\n");
            sb.Append("  \"certificate\": [");
            for (int i = 0; i < scr.Certificate.Count; i++)
            {
                var m = scr.Certificate[i];
                if (i > 0) sb.Append(",");
                sb.Append($"{{\"id\":{m.PieceId},\"from\":[{m.From.x},{m.From.y}],\"to\":[{m.To.x},{m.To.y}],\"rot\":{m.Rot}}}");
            }
            sb.Append("],\n");
            sb.Append($"  \"certificateValid\": {(scr.CertificateValid ? "true" : "false")},\n");
            sb.Append($"  \"certificateReachable\": {(scr.CertificateReachable ? "true" : "false")}\n");
            sb.Append("}\n");
            File.WriteAllText(Path.Combine(folder, $"level_{run:D4}.json"), sb.ToString());
        }

        static string Pieces(PuzzleState s)
        {
            var parts = new List<string>();
            foreach (var kv in s.Pieces)
                parts.Add($"{{\"id\":{kv.Value.Id},\"type\":\"{kv.Value.Type}\",\"cell\":[{kv.Key.x},{kv.Key.y}]," +
                          $"\"rot\":{kv.Value.Rot},\"decoy\":{(kv.Value.IsDecoy ? "true" : "false")}}}");
            return "[" + string.Join(",", parts) + "]";
        }
    }
}
