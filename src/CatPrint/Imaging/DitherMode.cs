namespace CatPrint.Imaging
{
    public enum DitherMode
    {
        Threshold,
        FloydSteinberg,
        Atkinson,
        OrderedBayer4x4,
        OrderedBayer8x8,
        Jarvis,
        Stucki,
        Burkes,
        Sierra,
        Random
    }

    public static class DitherModeExtensions
    {
        public static string ToDisplayName(this DitherMode mode) => mode switch
        {
            DitherMode.Threshold => "Порог",
            DitherMode.FloydSteinberg => "Флойд–Стейнберг",
            DitherMode.Atkinson => "Аткинсон",
            DitherMode.OrderedBayer4x4 => "Байер 4×4",
            DitherMode.OrderedBayer8x8 => "Байер 8×8",
            DitherMode.Jarvis => "Джарвис",
            DitherMode.Stucki => "Стуки",
            DitherMode.Burkes => "Берк",
            DitherMode.Sierra => "Сьерра",
            DitherMode.Random => "Случайный",
            _ => mode.ToString()
        };
    }
}
