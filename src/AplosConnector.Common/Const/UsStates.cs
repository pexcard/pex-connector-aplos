using System;
using System.Collections.Generic;

namespace AplosConnector.Common.Const;

// PEX's CreateVendor accepts only a 2-letter state code; Aplos contacts entered in its UI carry the full name.
public static class UsStates
{
    private static readonly Dictionary<string, string> States = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Alabama"] = "AL", ["Alaska"] = "AK", ["Arizona"] = "AZ", ["Arkansas"] = "AR",
        ["California"] = "CA", ["Colorado"] = "CO", ["Connecticut"] = "CT", ["Delaware"] = "DE",
        ["Florida"] = "FL", ["Georgia"] = "GA", ["Hawaii"] = "HI", ["Idaho"] = "ID",
        ["Illinois"] = "IL", ["Indiana"] = "IN", ["Iowa"] = "IA", ["Kansas"] = "KS",
        ["Kentucky"] = "KY", ["Louisiana"] = "LA", ["Maine"] = "ME", ["Maryland"] = "MD",
        ["Massachusetts"] = "MA", ["Michigan"] = "MI", ["Minnesota"] = "MN", ["Mississippi"] = "MS",
        ["Missouri"] = "MO", ["Montana"] = "MT", ["Nebraska"] = "NE", ["Nevada"] = "NV",
        ["New Hampshire"] = "NH", ["New Jersey"] = "NJ", ["New Mexico"] = "NM", ["New York"] = "NY",
        ["North Carolina"] = "NC", ["North Dakota"] = "ND", ["Ohio"] = "OH", ["Oklahoma"] = "OK",
        ["Oregon"] = "OR", ["Pennsylvania"] = "PA", ["Rhode Island"] = "RI", ["South Carolina"] = "SC",
        ["South Dakota"] = "SD", ["Tennessee"] = "TN", ["Texas"] = "TX", ["Utah"] = "UT",
        ["Vermont"] = "VT", ["Virginia"] = "VA", ["Washington"] = "WA", ["West Virginia"] = "WV",
        ["Wisconsin"] = "WI", ["Wyoming"] = "WY",
        ["District of Columbia"] = "DC",
        ["American Samoa"] = "AS", ["Guam"] = "GU", ["Marshall Islands"] = "MH",
        ["Northern Mariana Islands"] = "MP", ["Palau"] = "PW", ["Puerto Rico"] = "PR",
        ["Virgin Islands"] = "VI",
        ["Armed Forces Americas"] = "AA", ["Armed Forces Europe"] = "AE", ["Armed Forces Pacific"] = "AP",
    };

    private static readonly HashSet<string> Codes = new(States.Values, StringComparer.OrdinalIgnoreCase);

    public static string TryGetStateCode(string state)
    {
        if (string.IsNullOrWhiteSpace(state)) return null;

        var trimmed = state.Trim();
        if (Codes.Contains(trimmed)) return trimmed.ToUpperInvariant();

        return States.GetValueOrDefault(trimmed);
    }
}
