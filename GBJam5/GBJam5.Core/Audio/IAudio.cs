using System;

namespace GBJam5.Audio
{
    /// <summary>
    /// Represents an audio sample that can be started and stopped; e.g. a music
    /// track or ambient sound.
    /// </summary>
    public interface IAudio
        : IDisposable
    {
        /// <summary>
        /// Starts the audio sample playing. If the audio was previously
        /// stopped, play resumes from the same point in the track. Does
        /// nothing if the audio is currently playing.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the audio sample.
        /// </summary>
        void Stop();
    }
}
