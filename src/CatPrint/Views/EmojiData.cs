using System.Collections.Generic;

namespace CatPrint.Views
{
    public sealed class EmojiCategory
    {
        public string Title { get; }
        public List<string> Items { get; }

        public EmojiCategory(string title, string packed)
        {
            Title = title;
            Items = new List<string>(packed.Split(' ',
                System.StringSplitOptions.RemoveEmptyEntries));
        }
    }

    /// <summary>
    /// Большой набор как в Windows. Плюс на самой бумаге работает Win+. —
    /// системная панель со всеми эмодзи и поиском.
    /// Для термопечати (1 бит) чётче всего идут «Символы».
    /// </summary>
    public static class EmojiData
    {
        public static List<EmojiCategory> Categories { get; } = new()
        {
            new EmojiCategory("😀 Лица",
                "😀 😁 😂 🤣 😊 😍 😎 🤔 😴 😭 😡 🥳 😇 🙃 😉 🥺 😜 😝 🤪 😋 🤗 🤭 🤫 😶 😐 🙄 😬 😮 😲 😳 🥵 🥶 😱 😨 😰 😥 🤤 😪 😵 🤠 😷 🤒 🤕 🤢 🤮 🥴 🤯 🥱 😺 😸 😹 😻 🙀 😿 😼 🙈 🙉 🙊"),

            new EmojiCategory("👍 Жесты",
                "👍 👎 👏 🙏 ✌ 🤝 👋 💪 ☝ 👀 🫶 🤟 👌 ✊ 👐 🤲 ✋ 🤚 🖐 👊 🤛 🤜 👈 👉 👆 👇 ✍ 💅 🤳 💃 🕺 🚶 🏃 👯 🙇 💁 🙅 🙆 🤦 🤷 🧍 🧎 🙋 🤸 ⛹ 🧘"),

            new EmojiCategory("❤ Сердца",
                "❤ 🧡 💛 💚 💙 💜 🖤 🤍 💔 ❣ 💕 💞 💓 💗 💖 💘 💝 💟 💋 🌹 🥀 💐 🌺 🌷 🌸 💮 🏵"),

            new EmojiCategory("🐶 Животные",
                "🐶 🐱 🐭 🐹 🐰 🦊 🐻 🐼 🐨 🐯 🦁 🐮 🐷 🐸 🐵 🐒 🐔 🐧 🐦 🐤 🦆 🦅 🦉 🦇 🐺 🐗 🐴 🦄 🐝 🐛 🦋 🐌 🐞 🐜 🐢 🐍 🦎 🐙 🦑 🦐 🦞 🦀 🐡 🐠 🐟 🐬 🐳 🐋 🦈 🐊 🐅 🐆 🦓 🐘 🦛 🦏 🐪 🦒 🦘 🐎 🐖 🐏 🐑 🐐 🦌 🐕 🐩 🐈 🐓 🦃 🦚 🦜 🦢 🕊 🐇 🦝 🦨 🦡 🦦 🐁 🐀 🐿 🦔 🐾 🦂 🦟 🐚 🪲 🪳 🦗 🪰 🪱 🦠"),

            new EmojiCategory("🍕 Еда",
                "🍎 🍐 🍊 🍋 🍌 🍉 🍇 🍓 🫐 🍒 🍑 🥭 🍍 🥥 🥝 🍅 🥑 🍆 🥔 🥕 🌽 🌶 🥒 🥬 🥦 🧄 🍄 🥜 🌰 🍞 🥐 🥖 🥨 🥯 🥞 🧇 🧀 🍖 🍗 🥩 🍔 🍟 🍕 🌭 🥪 🌮 🌯 🥙 🥚 🍳 🍿 🍱 🍙 🍚 🍛 🍜 🍝 🍣 🍤 🍡 🥟 🍦 🍧 🍨 🍩 🍪 🎂 🍰 🧁 🍫 🍬 🍭 🍯 🍼 🥛 ☕ 🍵 🥤 🧋 🍺 🍻 🥂 🍷 🍸 🍹 🍾 ☕ 🫖"),

            new EmojiCategory("⚽ Активности",
                "⚽ 🏀 🏈 ⚾ 🎾 🏐 🏉 🎱 🏓 🏸 🏒 🏑 🥍 🏏 ⛳ 🏹 🎣 🥊 🥋 🛹 🛼 ⛸ 🎿 ⛷ 🏂 🏋 🤼 🤺 🏌 🏇 🏄 🏊 🚣 🧗 🚴 🤹 🎪 🎭 🎨 🎬 🎤 🎧 🎹 🥁 🎷 🎺 🎸 🎻 🎲 ♟ 🎯 🎳 🎮 🧩 🎰 🎸 🪕 🪗 🎺"),

            new EmojiCategory("✈ Путешествия",
                "🚗 🚕 🚙 🚌 🏎 🚓 🚑 🚒 🚐 🛻 🚚 🚜 🛴 🚲 🛵 🏍 🚨 🚍 🚘 🚖 🚡 🚠 🚃 🚋 🚞 🚝 🚄 🚂 🚇 ✈ 🛫 🛬 🛰 🚀 🛸 🚁 🛶 ⛵ 🚤 🛥 🛳 ⛴ 🚢 ⚓ ⛽ 🚧 🚦 🚏 🗺 🗽 🗼 🏰 🏯 🏟 🎡 🎢 🎠 ⛲ 🏖 🏝 🌋 ⛰ 🏔 🗻 🏕 ⛺ 🏠 🏡 🏢 🏥 🏦 🏨 🏪 🏫 🏭 💒 🌉 🌃 🌆 🌇 🎆 🎇"),

            new EmojiCategory("💡 Объекты",
                "💡 🔦 🕯 🧯 💸 💵 💰 💳 💎 ⚖ 🧰 🔧 🔨 ⚙ ⛓ 🧲 🔫 💣 🧨 🪓 🔪 🗡 ⚔ 🛡 📿 🔮 🧿 💈 🔭 🔬 💊 💉 🩹 🌡 🏷 🔖 🚩 🎫 🎟 🎖 🏆 🏅 🥇 🥈 🥉 💌 💣 📦 📯 📻 📱 📲 ☎ 📞 📟 📠 🔋 🪫 💻 🖥 🖨 ⌨ 🖱 🖲 💽 💾 💿 📀 🧮 🎥 📽 📺 📷 📸 📹 📼 🔍 🕰 ⏰ ⏱ ⏲ 🕛 🕐 🕑 🕒 🕓 🕔 🕕 🕖 🕗 🕘 🕙 🕚 ⌛ ⏳ ⌚ ⏱ 🧭 🗝 🔑 🪄 🎈 🎀 🎁 🎗 🏮 🪔 ✉ 📩 📨 📧 💌 📥 📤 📦 📫 📪 📮 🖊 🖋 ✒ 🖌 🖍 📝 ✏ 📏 📐 ✂ 📌 📍 🗑 🔒 🔓 🔏 🔐 🔑 🪚 🧷 🖇 📎"),

            new EmojiCategory("★ Символы — чётче всего",
                "★ ☆ ♥ ♦ ♣ ♠ ● ○ ▲ △ ■ □ ⬛ ⬜ ✔ ✖ → ← ↑ ↓ ➕ ➖ ✨ ❤ ☎ ⚙ ✂ ⛔ ☑ ☒ ◀ ▶ № © ® ™ ℹ ♻ ⚠ ☢ ☣ ⬆ ⬇ ➡ ⬅ ↔ ↕ 🔀 🔁 🔂 ▶ ⏸ ⏯ ⏹ ⏭ ⏮ 🔼 🔽 ⏫ ⏬ ▪ ▫ ◾ ◽ ◼ ◻ 🔴 🟠 🟡 🟢 🔵 🟣 ⚫ ⚪ 🟤 🔶 🔷 🔸 🔹 🔺 🔻 💠 ❇ ✳ ❎ ✅ ✔ ❌ ➰ ➿ ‼ ⁉ ❓ ❔ ❗ ❕ 💯 🔠 🔡 🔢 🆘 🅰 🅱 🆎 🆑 📛 🔞 📵 🚯 🔕 🔇 📣 📢 🔊 ⬆ 🆙 🆒 🆕 🆓 0⃣ 1⃣ 2⃣ 3⃣ 4⃣ 5⃣ 6⃣ 7⃣ 8⃣ 9⃣ 🔟 ☯ ☮ ✝ ☪ 🕉 ☸ ✡ ☦ ☪️ 🛐 ⛎ ♈ ♉ ♊ ♋ ♌ ♍ ♎ ♏ ♐ ♑ ♒ ♓ ⏏ ❤️‍🔥 ❤️‍🩹 ♾️")
        };
    }
}
