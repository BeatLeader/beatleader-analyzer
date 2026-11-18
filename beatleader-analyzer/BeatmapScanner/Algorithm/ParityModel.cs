using Analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Grid;
using System.Collections.Generic;

namespace Analyzer.BeatmapScanner.Algorithm
{
    /// <summary>
    /// The parity model (Parity/MlParityModel.cs, trained on top players' replays). When ParityModel.Current is set (the
    /// default), AnalyzeMap preprocesses both hands first (the model reads both), then calls Predict, which sets Forehand
    /// and ParityErrors on every cube like ParityPredictor.Predict (and may merge swings by changing the Head / Pattern /
    /// Tail flags, and give the swings' directions), then creates the swings. Null: ParityPredictor.
    /// </summary>
    public interface IParityModel
    {
        void Predict(ParityModelInput input);
    }

    /// <summary>What the parity model gets: both hands' cubes after PreprocessNotes (empty for a hand of 2 notes or
    /// fewer, which the Analyzer leaves out), and the map's bombs, walls, BPM and format version</summary>
    public class ParityModelInput
    {
        public List<Cube> Red { get; set; } = new List<Cube>();
        public List<Cube> Blue { get; set; } = new List<Cube>();
        public List<Bomb> Bombs { get; set; } = new List<Bomb>();
        public List<Wall> Walls { get; set; } = new List<Wall>();
        public float Bpm { get; set; }
        public string MapVersion { get; set; } = "";
    }

    public static class ParityModel
    {
        /// <summary>The model the Analyzer uses; null = ParityPredictor</summary>
        public static IParityModel Current { get; set; } = new Parity.MlParityModel();
    }
}
