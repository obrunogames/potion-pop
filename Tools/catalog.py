"""Single source of truth for the worlds and collection cards of Potion Pop!.

Each world is a group of 20 levels with its own theme: home-screen backdrop, blurred gameplay backdrop, accent color
and an album of 9 collectible cards (magical items). After the last world the themes cycle.
Used by Tools/art_manifest.py (image prompts) and Tools/export_catalog.py (Unity catalog + localized names).
"""

AREAS = [
    {
        "id": "forest",
        "names": {"en": "Enchanted Forest", "pt": "Floresta Encantada", "es": "Bosque Encantado"},
        "accent": "#2ED6A1",
        "home": (
            "a cozy witch's potion cottage built inside a giant red-and-white spotted mushroom in an enchanted forest, "
            "round glowing windows, a little wooden door with a crescent moon, potion bottles on the windowsill, "
            "glowing fireflies, crystal lanterns hanging from the trees, a mossy stone path and tiny blue flowers"
        ),
        "interior": "enchanted forest potion workshop with glowing mushrooms, wooden shelves of potions and fireflies",
        "tint": "mint green and warm gold",
        "cards": [
            ("glow_mushroom", "a cute glowing blue-and-teal toadstool mushroom with tiny sparkles",
             {"en": "Glow Shroom", "pt": "Cogumelo Brilhante", "es": "Hongo Brillante"}),
            ("firefly_jar", "a glass jar with a cork full of little glowing yellow fireflies",
             {"en": "Firefly Jar", "pt": "Pote de Vaga-lumes", "es": "Frasco de Luciérnagas"}),
            ("wise_owl", "a chubby baby owl with big round glasses and a tiny wizard hat",
             {"en": "Wise Owl", "pt": "Coruja Sábia", "es": "Búho Sabio"}),
            ("leaf_spellbook", "a thick magic spellbook with a green leaf-covered cover and a golden clasp",
             {"en": "Leaf Spellbook", "pt": "Livro de Folhas", "es": "Libro de Hojas"}),
            ("acorn_potion", "a round potion bottle shaped like an acorn with a green glowing liquid",
             {"en": "Acorn Potion", "pt": "Poção de Bolota", "es": "Poción de Bellota"}),
            ("fairy_lantern", "a delicate golden lantern with a tiny fairy light glowing inside",
             {"en": "Fairy Lantern", "pt": "Lanterna de Fada", "es": "Farol de Hada"}),
            ("magic_watering_can", "a little copper watering can pouring sparkling magic water",
             {"en": "Magic Watering Can", "pt": "Regador Mágico", "es": "Regadera Mágica"}),
            ("lucky_clover", "a shiny four-leaf clover charm with a golden ribbon",
             {"en": "Lucky Clover", "pt": "Trevo da Sorte", "es": "Trébol de la Suerte"}),
            ("baby_dragon", "a tiny chubby green baby dragon with little wings, sitting and smiling",
             {"en": "Baby Dragon", "pt": "Dragãozinho", "es": "Dragoncito"}),
        ],
    },
    {
        "id": "crystal",
        "names": {"en": "Crystal Caves", "pt": "Cavernas de Cristal", "es": "Cuevas de Cristal"},
        "accent": "#4FB3FF",
        "home": (
            "a sparkling crystal cave with a charming potion workshop carved into giant glowing amethyst and "
            "sapphire crystals, a small underground waterfall, glowing blue mushrooms, a wooden mine cart full of "
            "gems and crystal lamps"
        ),
        "interior": "crystal cave potion laboratory with glowing blue and purple crystals",
        "tint": "icy blue and lavender",
        "cards": [
            ("amethyst_cluster", "a cluster of glowing purple amethyst crystals",
             {"en": "Amethyst", "pt": "Ametista", "es": "Amatista"}),
            ("crystal_ball", "a glowing crystal ball with swirling blue mist on a golden stand",
             {"en": "Crystal Ball", "pt": "Bola de Cristal", "es": "Bola de Cristal"}),
            ("geode", "a cracked-open geode showing sparkling blue crystals inside",
             {"en": "Geode", "pt": "Geodo", "es": "Geoda"}),
            ("blue_lantern", "an old mining lantern with a magical blue flame",
             {"en": "Miner's Lantern", "pt": "Lampião Azul", "es": "Farol Azul"}),
            ("crystal_bat", "a cute round baby bat made of shiny lavender crystal",
             {"en": "Crystal Bat", "pt": "Morcego de Cristal", "es": "Murciélago de Cristal"}),
            ("starlight_potion", "a tall slim potion bottle with glowing starry blue liquid",
             {"en": "Starlight Potion", "pt": "Poção Estelar", "es": "Poción Estelar"}),
            ("rainbow_pickaxe", "a small pickaxe with a rainbow crystal head and wooden handle",
             {"en": "Rainbow Pickaxe", "pt": "Picareta Arco-íris", "es": "Pico Arcoíris"}),
            ("gem_ring", "a golden ring with a big sparkling blue diamond",
             {"en": "Gem Ring", "pt": "Anel de Gema", "es": "Anillo de Gema"}),
            ("gem_crown", "a small silver tiara with colorful gemstones",
             {"en": "Gem Tiara", "pt": "Tiara de Gemas", "es": "Tiara de Gemas"}),
        ],
    },
    {
        "id": "candy",
        "names": {"en": "Candy Kingdom", "pt": "Reino dos Doces", "es": "Reino de los Dulces"},
        "accent": "#FF7EB6",
        "home": (
            "a candy castle potion shop made of gingerbread with pink frosting roofs and lollipop towers, cotton "
            "candy trees, a small chocolate river with a candy bridge, giant swirl lollipops and gumdrop bushes"
        ),
        "interior": "pastel candy kingdom potion kitchen with jars of sweets and pink frosting",
        "tint": "pink and cream",
        "cards": [
            ("gingerbread_wizard", "a cute gingerbread man cookie wearing a little purple wizard hat",
             {"en": "Gingerbread Wizard", "pt": "Mago de Gengibre", "es": "Mago de Jengibre"}),
            ("candy_wand", "a candy cane magic wand with a pink star on top",
             {"en": "Candy Wand", "pt": "Varinha de Bala", "es": "Varita de Caramelo"}),
            ("choco_cauldron", "a small cauldron bubbling with melted chocolate and marshmallows",
             {"en": "Choco Cauldron", "pt": "Caldeirão de Chocolate", "es": "Caldero de Chocolate"}),
            ("lollipop_potion", "a potion bottle with swirly pink liquid and a lollipop as the cork",
             {"en": "Lollipop Potion", "pt": "Poção de Pirulito", "es": "Poción de Paleta"}),
            ("gummy_dragon", "a translucent red gummy candy dragon",
             {"en": "Gummy Dragon", "pt": "Dragão de Goma", "es": "Dragón de Gomita"}),
            ("cupcake_castle", "a cupcake shaped like a tiny castle with frosting towers",
             {"en": "Cupcake Castle", "pt": "Castelo de Cupcake", "es": "Castillo de Cupcake"}),
            ("rainbow_macaron", "a big rainbow-colored macaron with a sparkle",
             {"en": "Rainbow Macaron", "pt": "Macaron Arco-íris", "es": "Macaron Arcoíris"}),
            ("honey_drops", "a round glass jar of glowing golden honey candy drops",
             {"en": "Honey Drops", "pt": "Balas de Mel", "es": "Caramelos de Miel"}),
            ("sugar_unicorn", "a cute white unicorn made of sugar with a pastel rainbow mane",
             {"en": "Sugar Unicorn", "pt": "Unicórnio de Açúcar", "es": "Unicornio de Azúcar"}),
        ],
    },
    {
        "id": "sky",
        "names": {"en": "Sky Castle", "pt": "Castelo nas Nuvens", "es": "Castillo en las Nubes"},
        "accent": "#A98BFF",
        "home": (
            "a floating sky castle with a wizard observatory tower with a purple dome, sitting on fluffy white "
            "clouds, little rainbow bridges between floating islands, hot air balloons and colorful flags"
        ),
        "interior": "airy wizard tower observatory among the clouds with telescopes and star maps",
        "tint": "lavender and sky blue",
        "cards": [
            ("flying_broom", "a magic flying broomstick with a purple ribbon and sparkles",
             {"en": "Flying Broom", "pt": "Vassoura Voadora", "es": "Escoba Voladora"}),
            ("cloud_kitten", "a fluffy kitten made of white cloud with tiny wings",
             {"en": "Cloud Kitten", "pt": "Gatinho de Nuvem", "es": "Gatito de Nube"}),
            ("rainbow_potion", "a round potion flask with layered rainbow liquid",
             {"en": "Rainbow Potion", "pt": "Poção Arco-íris", "es": "Poción Arcoíris"}),
            ("hot_air_balloon", "a small striped hot air balloon in purple and yellow",
             {"en": "Hot Air Balloon", "pt": "Balão", "es": "Globo Aerostático"}),
            ("star_compass", "a golden compass with a glowing star needle",
             {"en": "Star Compass", "pt": "Bússola Estelar", "es": "Brújula Estelar"}),
            ("brass_telescope", "a shiny brass telescope on a tiny tripod",
             {"en": "Telescope", "pt": "Telescópio", "es": "Telescopio"}),
            ("magic_carpet", "a small flying magic carpet with purple and gold patterns and tassels",
             {"en": "Magic Carpet", "pt": "Tapete Mágico", "es": "Alfombra Mágica"}),
            ("lightning_jar", "a corked glass jar with a tiny glowing lightning bolt inside",
             {"en": "Lightning Jar", "pt": "Pote de Raio", "es": "Frasco de Rayo"}),
            ("winged_hourglass", "a golden hourglass with little white wings and blue sand",
             {"en": "Winged Hourglass", "pt": "Ampulheta Alada", "es": "Reloj de Arena Alado"}),
        ],
    },
    {
        "id": "lagoon",
        "names": {"en": "Coral Lagoon", "pt": "Lagoa de Coral", "es": "Laguna de Coral"},
        "accent": "#2EC9D6",
        "home": (
            "a sunny tropical lagoon with a potion shop built inside a giant pink spiral seashell on a small coral "
            "island, turquoise water, palm trees, colorful corals, a wooden pier with potion barrels and a tiny "
            "lighthouse"
        ),
        "interior": "underwater coral potion lab with bubbles, glowing jellyfish and seashells",
        "tint": "turquoise and coral pink",
        "cards": [
            ("mermaid_potion", "a seashell-shaped potion bottle with shimmering teal liquid",
             {"en": "Mermaid Potion", "pt": "Poção de Sereia", "es": "Poción de Sirena"}),
            ("magic_shell", "a glowing pink spiral seashell with sparkles",
             {"en": "Magic Shell", "pt": "Concha Mágica", "es": "Concha Mágica"}),
            ("pearl_clam", "an open clam with a big shiny pearl inside",
             {"en": "Pearl Clam", "pt": "Ostra com Pérola", "es": "Almeja con Perla"}),
            ("octopus_wizard", "a cute baby purple octopus wearing a tiny wizard hat",
             {"en": "Octopus Wizard", "pt": "Polvo Mago", "es": "Pulpo Mago"}),
            ("treasure_chest", "a small wooden treasure chest overflowing with gold coins and pearls",
             {"en": "Sea Treasure", "pt": "Tesouro do Mar", "es": "Tesoro del Mar"}),
            ("wand_starfish", "a smiling orange starfish holding a tiny magic wand",
             {"en": "Starfish", "pt": "Estrela-do-mar", "es": "Estrella de Mar"}),
            ("coral_crown", "a crown made of pink and orange coral with pearls",
             {"en": "Coral Crown", "pt": "Coroa de Coral", "es": "Corona de Coral"}),
            ("bubble_potion", "a round potion bottle full of floating bubbles and blue liquid",
             {"en": "Bubble Potion", "pt": "Poção de Bolhas", "es": "Poción de Burbujas"}),
            ("seahorse", "a cute golden seahorse with a curly tail",
             {"en": "Seahorse", "pt": "Cavalo-marinho", "es": "Caballito de Mar"}),
        ],
    },
    {
        "id": "moon",
        "names": {"en": "Moonlit Village", "pt": "Vila do Luar", "es": "Aldea de la Luna"},
        "accent": "#FFA94D",
        "home": (
            "a charming moonlit village with a potion shop shaped like a big friendly orange pumpkin with glowing "
            "round windows and a curly chimney, cute smiling jack-o-lanterns, crooked lamp posts, a purple night sky "
            "with a big friendly yellow moon and twinkling stars (cute and cozy, not scary)"
        ),
        "interior": "cozy moonlit potion shop with candles, pumpkins and purple night light",
        "tint": "warm orange and deep purple",
        "cards": [
            ("happy_pumpkin", "a cute smiling jack-o-lantern pumpkin glowing warmly",
             {"en": "Happy Pumpkin", "pt": "Abóbora Feliz", "es": "Calabaza Feliz"}),
            ("star_witch_hat", "a floppy purple witch hat with golden stars and a moon charm",
             {"en": "Witch Hat", "pt": "Chapéu de Bruxa", "es": "Sombrero de Bruja"}),
            ("moon_potion", "a crescent-moon shaped potion bottle with glowing silver liquid",
             {"en": "Moon Potion", "pt": "Poção da Lua", "es": "Poción de Luna"}),
            ("friendly_ghost", "a cute round little white ghost with rosy cheeks and a smile",
             {"en": "Friendly Ghost", "pt": "Fantasminha", "es": "Fantasmita"}),
            ("broom_kitten", "a tiny black kitten riding a little broomstick",
             {"en": "Broom Kitten", "pt": "Gatinho na Vassoura", "es": "Gatito en Escoba"}),
            ("bat_cupcake", "a chocolate cupcake with purple frosting and little bat wings",
             {"en": "Bat Cupcake", "pt": "Cupcake Morcego", "es": "Cupcake Murciélago"}),
            ("green_cauldron", "a black cauldron bubbling with glowing green potion",
             {"en": "Bubbling Cauldron", "pt": "Caldeirão Borbulhante", "es": "Caldero Burbujeante"}),
            ("web_dreamcatcher", "a dreamcatcher with a sparkly silver web and purple feathers",
             {"en": "Dreamcatcher", "pt": "Filtro dos Sonhos", "es": "Atrapasueños"}),
            ("candle_lantern", "a little iron lantern with a warm glowing candle",
             {"en": "Candle Lantern", "pt": "Lanterna de Vela", "es": "Farol de Vela"}),
        ],
    },
]


def all_cards():
    for area in AREAS:
        for cid, desc, names in area["cards"]:
            yield area, cid, desc, names
