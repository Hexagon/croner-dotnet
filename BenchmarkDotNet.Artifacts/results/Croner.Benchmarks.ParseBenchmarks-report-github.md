```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.115
  [Host]   : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.15 (10.0.15, 10.0.1526.53008), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method   | Mean        | Error        | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------- |------------:|-------------:|----------:|------:|--------:|-------:|----------:|------------:|
| Croner   | 1,435.77 ns |   388.308 ns | 21.284 ns |  1.00 |    0.02 | 0.0477 |    6664 B |       1.000 |
| Cronos   |    30.17 ns |     9.966 ns |  0.546 ns |  0.02 |    0.00 | 0.0003 |      48 B |       0.007 |
| NCrontab |   710.59 ns | 1,014.749 ns | 55.622 ns |  0.49 |    0.03 | 0.0134 |    1816 B |       0.273 |
