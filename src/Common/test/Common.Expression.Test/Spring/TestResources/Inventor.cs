// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#pragma warning disable S4004 // Collection properties should be readonly
// ReSharper disable InconsistentNaming

using System.Globalization;

namespace Steeltoe.Common.Expression.Test.Spring.TestResources;

public sealed class Inventor
{
    private PlaceOfBirth _placeOfBirth;
    public List<int> ListOfInteger { get; set; } = new();
    public List<bool> BoolList { get; } = new();
    public Dictionary<string, bool> MapOfStringToBoolean { get; } = new();
    public Dictionary<int, string> MapOfNumbersUpToTen { get; } = new();
    public List<int> ListOfNumbersUpToTen { get; } = new();
    public List<int> ListOneFive { get; } = new();

    public string[] StringArrayOfThreeItems { get; } =
    {
        "1",
        "2",
        "3"
    };

    public int Counter { get; set; }
    public string RandomField { get; }
    public Dictionary<string, string> TestDictionary { get; }
    public string PublicName { get; set; }
    public ArrayContainer ArrayContainer { get; set; }
    public bool PublicBoolean { get; set; }

    public string[] Inventions { get; set; }

    public PlaceOfBirth PlaceOfBirth
    {
        get => _placeOfBirth;
        set
        {
            _placeOfBirth = value;

            PlacesLived = new[]
            {
                value
            };

            PlacesLivedList.Add(value);
        }
    }

    public string Name { get; }

    public bool WonNobelPrize { get; set; }

    public PlaceOfBirth[] PlacesLived { get; set; }

    public List<PlaceOfBirth> PlacesLivedList { get; set; } = new();

    public bool SomeProperty { get; set; }

    public DateTime BirthDate { get; }

    public string Foo { get; set; }

    public string Nationality { get; }

    public Inventor(params string[] strings)
    {
    }

    public Inventor(string name, DateTime birthdate, string nationality)
    {
        Name = name;
        _name = name;
        _name_ = name;
        BirthDate = birthdate;
        Nationality = nationality;
        ArrayContainer = new ArrayContainer();

        TestDictionary = new Dictionary<string, string>
        {
            { "monday", "montag" },
            { "tuesday", "dienstag" },
            { "wednesday", "mittwoch" },
            { "thursday", "donnerstag" },
            { "friday", "freitag" },
            { "saturday", "samstag" },
            { "sunday", "sonntag" }
        };

        ListOneFive.Add(1);
        ListOneFive.Add(5);
        BoolList.Add(false);
        BoolList.Add(false);
        ListOfNumbersUpToTen.Add(1);
        ListOfNumbersUpToTen.Add(2);
        ListOfNumbersUpToTen.Add(3);
        ListOfNumbersUpToTen.Add(4);
        ListOfNumbersUpToTen.Add(5);
        ListOfNumbersUpToTen.Add(6);
        ListOfNumbersUpToTen.Add(7);
        ListOfNumbersUpToTen.Add(8);
        ListOfNumbersUpToTen.Add(9);
        ListOfNumbersUpToTen.Add(10);
        MapOfNumbersUpToTen.Add(1, "one");
        MapOfNumbersUpToTen.Add(2, "two");
        MapOfNumbersUpToTen.Add(3, "three");
        MapOfNumbersUpToTen.Add(4, "four");
        MapOfNumbersUpToTen.Add(5, "five");
        MapOfNumbersUpToTen.Add(6, "six");
        MapOfNumbersUpToTen.Add(7, "seven");
        MapOfNumbersUpToTen.Add(8, "eight");
        MapOfNumbersUpToTen.Add(9, "nine");
        MapOfNumbersUpToTen.Add(10, "ten");
    }

    public int ThrowException(int valueIn)
    {
        Counter++;

        if (valueIn == 1)
        {
            throw new ArgumentException("ArgumentException for 1", nameof(valueIn));
        }

        if (valueIn == 2)
        {
            throw new SystemException("RuntimeException for 2");
        }

        if (valueIn == 4)
        {
            throw new TestException();
        }

        return valueIn;
    }

    public string ThrowException(PlaceOfBirth pob)
    {
        return pob.City;
    }

    public string Echo(object o)
    {
        return o.ToString();
    }

    public string SayHelloTo(string person)
    {
        return $"hello {person}";
    }

    public string PrintDouble(double d)
    {
        return d.ToString("F2", CultureInfo.InvariantCulture);
    }

    public string PrintDoubles(double[] d)
    {
        return $"{{{string.Join(", ", d.Select(x => x.ToString(CultureInfo.InvariantCulture)))}}}";
    }

    public List<string> GetDoublesAsStringList()
    {
        var result = new List<string>
        {
            "14.35",
            "15.45"
        };

        return result;
    }

    public string JoinThreeStrings(string a, string b, string c)
    {
        return a + b + c;
    }

    public int AVarargsMethod(params string[] strings)
    {
        if (strings == null)
        {
            return 0;
        }

        return strings.Length;
    }

    public int AVarargsMethod2(int i, params string[] strings)
    {
        if (strings == null)
        {
            return i;
        }

        return strings.Length + i;
    }

    public sealed class TestException : Exception
    {
    }
#pragma warning disable SA1401 // Fields should be private
    public string _name;
    public string _name_;
#pragma warning restore SA1401 // Fields should be private
}
