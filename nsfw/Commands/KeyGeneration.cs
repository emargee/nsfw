﻿namespace Nsfw.Commands;

[Flags]
public enum KeyGeneration : byte
{
    U10 = 0,  //< 1.0.0 - 2.3.0.
    U30 = 2,  //< 3.0.0.
    U301 = 3,  //< 3.0.1 - 3.0.2.
    U40 = 4,  //< 4.0.0 - 4.1.0.
    U50 = 5,  //< 5.0.0 - 5.1.0.
    U60 = 6,  //< 6.0.0 - 6.1.0.
    U62 = 7,  //< 6.2.0.
    U70 = 8,  //< 7.0.0 - 8.0.1.
    U81 = 9,  //< 8.1.0 - 8.1.1.
    U90 = 10, //< 9.0.0 - 9.0.1.
    U91 = 11, //< 9.1.0 - 12.0.3.
    U121 = 12, //< 12.1.0.
    U130 = 13, //< 13.0.0 - 13.2.1.
    U140 = 14, //< 14.0.0 - 14.1.2.
    U150 = 15, //< 15.0.0 - 15.0.1.
    U160 = 16, //< 16.0.0 - 16.1.0.
    U170 = 17, //< 17.0.0+.
}