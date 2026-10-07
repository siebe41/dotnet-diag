using System;
using System.Collections.Generic;

var totals = new List<int> { 12, 7, 30 };
var large = totals.Where(t => t > 10);
Console.WriteLine(string.Join(",", large));
