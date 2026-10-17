using System;
using System.Collections.Generic;

namespace Sproutshot.MapGen
{
    /// <summary>
    /// Square-grid coordinate. Rows grow upward. Eight directions start at east,
    /// then turn counterclockwise: E, NE, N, NW, W, SW, S, SE.
    /// </summary>
    [Serializable]
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public readonly int Col;
        public readonly int Row;

        public GridCoord(int col, int row)
        {
            Col = col;
            Row = row;
        }

        public GridCoord Neighbour(int direction)
        {
            direction = ((direction % 8) + 8) % 8;
            switch (direction)
            {
                case 0: return new GridCoord(Col + 1, Row);
                case 1: return new GridCoord(Col + 1, Row + 1);
                case 2: return new GridCoord(Col, Row + 1);
                case 3: return new GridCoord(Col - 1, Row + 1);
                case 4: return new GridCoord(Col - 1, Row);
                case 5: return new GridCoord(Col - 1, Row - 1);
                case 6: return new GridCoord(Col, Row - 1);
                default: return new GridCoord(Col + 1, Row - 1);
            }
        }

        public IEnumerable<GridCoord> Neighbours()
        {
            for (int direction = 0; direction < 8; direction++)
                yield return Neighbour(direction);
        }

        public int DistanceTo(GridCoord other)
        {
            // Number of steps when cardinal and diagonal moves both count as one.
            return Math.Max(Math.Abs(Col - other.Col), Math.Abs(Row - other.Row));
        }

        public int DirectionToAdjacent(GridCoord other)
        {
            for (int direction = 0; direction < 8; direction++)
                if (Neighbour(direction) == other) return direction;
            return -1;
        }

        public bool Equals(GridCoord other) => Col == other.Col && Row == other.Row;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => unchecked(Col * 397 ^ Row);
        public override string ToString() => $"({Col},{Row})";
        public static bool operator ==(GridCoord left, GridCoord right) => left.Equals(right);
        public static bool operator !=(GridCoord left, GridCoord right) => !left.Equals(right);
    }
}
