﻿using System;
using System.Collections.Generic;
using System.Text;

namespace PokemonGoRaidBot.Data.Objects
{
    public class PokemonEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public List<RaidPostEntity> Posts { get; set; }
    }
}
