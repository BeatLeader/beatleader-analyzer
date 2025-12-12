using System;

namespace beatleader_analyzer.BeatmapScanner.Helper
{
    /// <summary>
    /// Converts Beat Saber grid positions to meter-based world coordinates.
    /// Grid Y=0 starts at the bottom of the play space.
    /// </summary>
    internal class GridPosition
    {
        // X-axis spacing: 0.6m between adjacent columns
        private const double X_SPACING = 0.6;
        
        // Y-axis: Grid Y=0 is at -0.275m (bottom of bottom row)
        private const double Y_NOTE_HALF_HEIGHT = 0.275;
        
        // Y-axis spacing between row centers
        private const double Y_BOTTOM_TO_MIDDLE = 0.55; // Distance from row 0 center to row 1 center
        private const double Y_MIDDLE_TO_TOP = 0.5;     // Distance from row 1 center to row 2 center
        
        /// <summary>
        /// Converts a grid X coordinate to meters.
        /// Grid center is at x=0 meters. Standard grid positions:
        /// x=-1 -> -1.5m, x=0 -> -0.9m, x=1 -> -0.3m, x=2 -> 0.3m, x=3 -> 0.9m
        /// </summary>
        public static double GridXToMeters(int gridX)
        {
            // Grid x=0 is at -0.9m (left of center)
            // Grid x=1.5 would be at 0m (center)
            // Each grid unit is 0.6m
            return (gridX - 1.5) * X_SPACING;
        }

        /// <summary>
        /// Converts a grid X coordinate (with decimal precision) to meters.
        /// </summary>
        public static double GridXToMeters(double gridX)
        {
            return (gridX - 1.5) * X_SPACING;
        }

        /// <summary>
        /// Converts a grid Y coordinate to meters.
        /// Grid Y=0 starts at bottom (-0.275m), row centers at 0, 0.55, 1.05m.
        /// Y values outside [0, 2] range return the edge values.
        /// </summary>
        public static double GridYToMeters(int gridY)
        {
            // Clamp to valid range
            if (gridY < 0)
            {
                return -Y_NOTE_HALF_HEIGHT;
            }
            if (gridY > 2)
            {
                return Y_BOTTOM_TO_MIDDLE + Y_MIDDLE_TO_TOP + Y_NOTE_HALF_HEIGHT;
            }
            
            return gridY switch
            {
                0 => 0.0,
                1 => Y_BOTTOM_TO_MIDDLE,
                2 => Y_BOTTOM_TO_MIDDLE + Y_MIDDLE_TO_TOP,
                _ => 0.0
            };
        }

        /// <summary>
        /// Converts a grid Y coordinate (with decimal precision) to meters.
        /// Grid Y=0 is at center of bottom row (0m).
        /// Y values outside [0, 2] range are clamped.
        /// Linear interpolation between rows.
        /// </summary>
        public static double GridYToMeters(double gridY)
        {
            // Clamp to valid range
            if (gridY < 0.0)
            {
                return -Y_NOTE_HALF_HEIGHT;
            }
            if (gridY > 2.0)
            {
                return Y_BOTTOM_TO_MIDDLE + Y_MIDDLE_TO_TOP + Y_NOTE_HALF_HEIGHT;
            }
            
            if (gridY <= 1.0)
            {
                // Between row 0 and row 1: linear interpolation
                return gridY * Y_BOTTOM_TO_MIDDLE;
            }
            else
            {
                // Between row 1 and row 2: linear interpolation
                double fraction = gridY - 1.0;
                return Y_BOTTOM_TO_MIDDLE + fraction * Y_MIDDLE_TO_TOP;
            }
        }

        /// <summary>
        /// Converts grid coordinates to meter-based world position.
        /// </summary>
        public static (double x, double y) GridToMeters(int gridX, int gridY)
        {
            return (GridXToMeters(gridX), GridYToMeters(gridY));
        }

        /// <summary>
        /// Converts grid coordinates (with decimal precision) to meter-based world position.
        /// </summary>
        public static (double x, double y) GridToMeters(double gridX, double gridY)
        {
            return (GridXToMeters(gridX), GridYToMeters(gridY));
        }
    }
}
