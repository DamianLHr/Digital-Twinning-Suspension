using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RealSize
{
    [System.Serializable]
    public struct SizeUnit : IEquatable<SizeUnit>
    {
        public static SizeUnit unity => new SizeUnit("unity", -1);

        public string unitName;
        public float unitsPerMeter;

        public SizeUnit(string name, float unityUnits)
        {
            unitName = name;
            unitsPerMeter = unityUnits;
        }


        public bool Equals(SizeUnit other)
        {
            if(this.unitName == other.unitName && this.unitsPerMeter == other.unitsPerMeter)
            {
                return true;
            }

            return false;
        }
    }
}
