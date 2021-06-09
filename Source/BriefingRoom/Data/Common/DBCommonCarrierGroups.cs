﻿/*
==========================================================================
This file is part of Briefing Room for DCS World, a mission
generator for DCS World, by @akaAgar (https://github.com/akaAgar/briefing-room-for-dcs)

Briefing Room for DCS World is free software: you can redistribute it
and/or modify it under the terms of the GNU General Public License
as published by the Free Software Foundation, either version 3 of
the License, or (at your option) any later version.

Briefing Room for DCS World is distributed in the hope that it will
be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with Briefing Room for DCS World. If not, see https://www.gnu.org/licenses/
==========================================================================
*/

using System;

namespace BriefingRoom4DCS.Data
{
    /// <summary>
    /// Stores information about carrier groups.
    /// </summary>
    internal class DBCommonCarrierGroup : IDisposable
    {
        /// <summary>
        /// Number of carriers beyond which carrier group will have the maximum number of escorts.
        /// </summary>
        internal const int ESCORT_FAMILIES_SHIP_COUNT = 3;

        /// <summary>
        /// Carrier course length (in nautical miles).
        /// </summary>
        internal double CourseLength { get; }

        /// <summary>
        /// Families escort families belongs to.
        /// </summary>
        internal UnitFamily[][] EscortUnitFamilies { get; }

        /// <summary>
        /// Target wind on deck (in knots).
        /// </summary>
        internal double IdealWindOfDeck { get; }

        /// <summary>
        /// Minimum carrier speed (in knots).
        /// </summary>
        internal double MinimumCarrierSpeed { get; }

        /// <summary>
        /// Distance between ships of a carrier group (in nautical miles).
        /// </summary>
        internal double ShipSpacing { get; }

        /// <summary>
        /// Constructor.
        /// </summary>
        internal DBCommonCarrierGroup()
        {
            using (INIFile ini = new INIFile($"{BRPaths.DATABASE}CarrierGroup.ini"))
            {
                CourseLength = Math.Max(5.0, ini.GetValue<double>("CarrierGroup", "CourseLength")) * Toolbox.NM_TO_METERS;
                IdealWindOfDeck = Math.Max(0.0, ini.GetValue<double>("CarrierGroup", "IdealWindOfDeck")) * Toolbox.KNOTS_TO_METERS_PER_SECOND;
                MinimumCarrierSpeed = Math.Max(0.0, ini.GetValue<double>("CarrierGroup", "MinimumCarrierSpeed")) * Toolbox.KNOTS_TO_METERS_PER_SECOND;
                ShipSpacing = Math.Max(0.1, ini.GetValue<double>("CarrierGroup", "ShipSpacing")) * Toolbox.NM_TO_METERS;

                EscortUnitFamilies = new UnitFamily[ESCORT_FAMILIES_SHIP_COUNT][];
                for (int i = 0; i < ESCORT_FAMILIES_SHIP_COUNT; i++)
                    EscortUnitFamilies[i] = ini.GetValueArray<UnitFamily>(
                        "CarrierGroup",
                        "EscortUnitFamilies.CarrierCount" + ((i == ESCORT_FAMILIES_SHIP_COUNT - 1) ? "Max" : $"{i + 1}"));
            }
        }

        /// <summary>
        /// <see cref="IDisposable"/> implementation.
        /// </summary>
        public void Dispose() { }
    }
}