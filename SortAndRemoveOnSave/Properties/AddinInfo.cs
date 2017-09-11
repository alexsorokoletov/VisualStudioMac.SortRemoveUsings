using System;
using Mono.Addins;
using Mono.Addins.Description;

[assembly: Addin(
    "SortAndRemoveOnSave",
    Namespace = "SortAndRemoveOnSave",
    Version = "1.1"
)]

[assembly: AddinName("SortAndRemoveOnSave")]
[assembly: AddinCategory("IDE extensions")]
[assembly: AddinDescription("Visual Studio for Mac Add-in to sort and remove usings when saving C# file")]
[assembly: AddinAuthor("Alex Sorokoletov")]
