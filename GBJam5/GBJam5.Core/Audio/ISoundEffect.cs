namespace GBJam5.Audio
{
    /// <summary>
    /// Represents an audio sample or sound effect that can be played multiple
    /// times, and may overlap with itself.
    /// </summary>
    public interface ISoundEffect
    {
        /// <summary>
        /// Sets the volume of the sound effect when next played.
        /// </summary>
        float Volume
        {
            get;
            set;
        }

        /// <summary>
        /// Plays the sound effect. If the effect has already been started, a
        /// new instance is played and may overlap with the earlier instance.
        /// </summary>
        void Play();
    }
}
