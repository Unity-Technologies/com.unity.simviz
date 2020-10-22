namespace UnityEngine.SimViz.Content.Utilities
{
    public static class RandomUtility
    {
        const uint k_LargePrime = 0x202A96CF;

        /// <summary>
        /// Returns a new Random object for parallel job instances
        /// </summary>
        /// <param name="baseRandomSeed">A base random value used to initialize the Random object's state</param>
        /// <param name="index">The parallel job's index</param>
        /// <param name="largePrime">
        ///     Used to salt the initial state of the returned Random object. This value should be a sufficiently large
        ///     prime number to ensure that the initial state is properly offset.
        /// </param>
        public static uint ParallelForRandomSeed(uint baseRandomSeed, int index, uint largePrime = k_LargePrime)
        {
            return (uint) ((baseRandomSeed + 1) * (index + 1) * largePrime);
        }

        public static Unity.Mathematics.Random ParallelForRandom(
            uint baseRandomSeed, int index, uint largePrime = k_LargePrime)
        {
            var seed = ParallelForRandomSeed(baseRandomSeed, index, largePrime);
            return new Unity.Mathematics.Random(seed);
        }

        public static uint CombineSeedWithBaseSeed(uint baseRandomSeed, uint randomSeed)
        {
            return baseRandomSeed ^ randomSeed;
        }
    }
}
