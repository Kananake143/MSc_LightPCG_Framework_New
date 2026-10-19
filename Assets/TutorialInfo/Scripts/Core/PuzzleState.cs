using System.Collections.Generic;
using UnityEngine;

namespace LightPCG.Core
{
    /// <summary>
    /// Movement rule of the game (Chapter 3, Section 3.2.4). ONE definition used by
    /// PuzzleState.Apply, PuzzleScrambler, MobilityAnalyzer, BotPlanner and PuzzleBot,
    /// so the certificate, the mobility descriptors and the bot all obey the same rule.
    /// Study setting: a component may be rotated in place, or carried ONE cell
    /// (to a 4-adjacent free interior cell) and placed with any permitted orientation.
    /// Change these values only for a sensitivity analysis, and before generating levels.
    /// </summary>
    public static class MovementRule
    {
        public const int StudyCarryDistance = 1;
        public static bool AllowRelocation = true;            // false = rotate-only
        public static int MaxCarryDistance = StudyCarryDistance; // 0 = anywhere; k > 0 = Manhattan distance <= k

        public static bool CarryOk(Vector2Int from, Vector2Int to)
        {
            if (from == to) return true;                        // rotation in place
            if (!AllowRelocation) return false;
            if (MaxCarryDistance <= 0) return true;
            return Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y) <= MaxCarryDistance;
        }

        public static bool IsStudyRule => AllowRelocation && MaxCarryDistance == StudyCarryDistance;

        public static string Describe()
            => !AllowRelocation ? "RotateOnly" : MaxCarryDistance <= 0 ? "Free" : "Carry" + MaxCarryDistance;
    }

    /// <summary>A movable optical component.</summary>
    public struct Piece
    {
        public int Id;            // stable id across moves
        public TileType Type;     // Mirror or Refractor
        public int Rot;           // allowed orientation (see OpticsRules)
        public bool IsDecoy;      // true = not part of the intended solution

        public Piece WithRot(int r) { var p = this; p.Rot = r; return p; }
    }

    /// <summary>
    /// Formal puzzle state s = (B, O, P, R, E, G):
    ///   B (board: walls, door), E (emitter cell + direction), G (receiver cell)
    ///   are fixed and taken from GridModel / fields below;
    ///   O, P, R (types, positions, rotations of movable pieces) are in Pieces.
    /// </summary>
    public sealed class PuzzleState
    {
        public Dictionary<Vector2Int, Piece> Pieces = new Dictionary<Vector2Int, Piece>();

        public PuzzleState Clone()
            => new PuzzleState { Pieces = new Dictionary<Vector2Int, Piece>(Pieces) };

        public bool TryFind(int id, out Vector2Int cell, out Piece piece)
        {
            foreach (var kv in Pieces)
                if (kv.Value.Id == id) { cell = kv.Key; piece = kv.Value; return true; }
            cell = default; piece = default; return false;
        }

        /// <summary>Apply a move (relocation and/or rotation). Returns false if illegal,
        /// including a relocation farther than MovementRule allows.</summary>
        public bool Apply(Move m, GridModel board)
        {
            if (!Pieces.TryGetValue(m.From, out var p) || p.Id != m.PieceId) return false;
            if (!OpticsRules.IsAllowedRotation(p.Type, m.Rot)) return false;
            if (m.From != m.To)
            {
                if (!MovementRule.CarryOk(m.From, m.To)) return false;
                if (m.To.x < 1 || m.To.y < 1 || m.To.x >= board.Width - 1 || m.To.y >= board.Height - 1) return false;
                if (Pieces.ContainsKey(m.To)) return false;
                if (board.GetTile(m.To.x, m.To.y) != TileType.Empty &&
                    !OpticsRules.IsMovable(board.GetTile(m.To.x, m.To.y))) return false;
                Pieces.Remove(m.From);
            }
            Pieces[m.To] = p.WithRot(m.Rot);
            return true;
        }
    }

    /// <summary>One legal action: pick piece at From, place at To with rotation Rot.
    /// From == To means rotate in place.</summary>
    public struct Move
    {
        public int PieceId;
        public Vector2Int From, To;
        public int Rot;
        public bool IsRotateOnly => From == To;
        public override string ToString() => $"#{PieceId} {From}->{To} @{Rot}";
    }

    public struct BeamResult
    {
        public List<Vector2Int> Path;
        public List<int> HitPieceIds;   // ordered sequence of pieces the beam interacts with
        public bool HitReceiver;
    }

    /// <summary>Pure logical beam trace on the grid (no Unity physics).</summary>
    public static class BeamTracer
    {
        public static BeamResult Trace(GridModel board, Vector2Int emitter, Vector2Int emitDir,
                                       IReadOnlyDictionary<Vector2Int, Piece> pieces)
        {
            var r = new BeamResult { Path = new List<Vector2Int>(), HitPieceIds = new List<int>() };
            var pos = emitter; var dir = emitDir;
            var seen = new HashSet<(Vector2Int, Vector2Int)>();
            int limit = board.Width * board.Height * 4;
            for (int i = 0; i < limit; i++)
            {
                pos += dir;
                if (pos.x < 0 || pos.y < 0 || pos.x >= board.Width || pos.y >= board.Height) break;
                if (!seen.Add((pos, dir))) break;       // loop
                r.Path.Add(pos);

                TileType t = board.GetTile(pos.x, pos.y);
                // Door blocks the beam, as in LaserSystem (non-optical tag -> stop).
                if (t == TileType.Wall || t == TileType.Emitter || t == TileType.Door) break;
                if (t == TileType.Receiver) { r.HitReceiver = true; break; }

                if (pieces.TryGetValue(pos, out var p))
                {
                    r.HitPieceIds.Add(p.Id);
                    dir = OpticsRules.Deflect(p.Type, p.Rot, dir);
                }
            }
            return r;
        }
    }
}
