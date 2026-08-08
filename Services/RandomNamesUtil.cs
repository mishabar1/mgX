using System;

namespace MG.Server.Services
{
    public class RandomNamesUtil
    {
        static Random random = new Random();

        // Animals here match the 3D head models under assets/heads/animals/<animal>.glb
        // (Quaternius "Animated Animal Pack", CC0). The name is "<Color> <Animal>", so the
        // last word selects the model and the colour word tints it.
        static List<string> animalsList = new List<string>
        {
            // Quaternius low-poly full-body animals (GLB) under assets/heads/animals/<animal>.glb
            "Cow",
            "Donkey",
            "Deer",
            "Alpaca",
            "Bull",
            "Fox",
            "Shiba",
            "Husky",
            "Stag",
            "Wolf",
            "Horse",
            "Pony",
        };
        static List<string> colorNames = new List<string>
        {
            "Red",
            "Blue",
            "Green",
            "Yellow",
            "Orange",
            "Purple",
            "Pink",
            "Brown",
            "Gray",
            "Black",
            "White",
            "Cyan",
            "Magenta",
            "Lavender",
            "Turquoise",
            "Beige",
            "Maroon",
            "Teal",
            "Gold",
            "Silver",
            "Indigo",
            "Violet",
            "Crimson",
            "Olive",
            "Aqua",
            "Navy",
            "Salmon",
            "Plum",
            "Mint",
            "Slate",
            "Coral",
            "Lime",
            "Khaki",
            "Orchid",
            "Periwinkle",
            "Peach",
            "Sienna",
            "Ruby",
            "Amber",
            "Emerald",
            "Sapphire",
            "Ivory",
            "Tangerine",
            "Mauve",
            "Cerulean",
            "Apricot",
            "Lilac",
            "Crimson",
            "Rust",
            "Cobalt",
            "Hazel",
            "Topaz",
            "Onyx",
            "Burgundy",
            "Sage",
            "Saffron",
            "Aubergine",
            "Caramel",
            "Denim",
            "Magenta",
            "Cyan",
            "Olive",
            "Platinum",
            "Bisque",
            "Amaranth",
            "Azure",
            "Cadmium",
            "Cerise",
            "Tawny",
            "Verdigris",
            "Viridian",
            "Cinnabar",
            "Moccasin",
            "Terra Cotta",
            "Chartreuse"
        };

        public static string GetName()
        {
            string animal = animalsList[random.Next(animalsList.Count)];
            string color = colorNames[random.Next(colorNames.Count)];
            return $"{color} {animal}";
        }
    }
}
