﻿using System;
using System.IO;
using System.Runtime.CompilerServices;
using EmbedResourceCSharp;
using Xunit;

namespace FileTests
{
    public partial class EmbedTests
    {
        private static string GetCurrentFilePath([CallerFilePath] string path = "") => path;
        private readonly string currentFolder;

        [FileEmbed("a.txt")]
        private static partial ReadOnlySpan<byte> GetA();

        public EmbedTests()
        {
            currentFolder = Path.GetDirectoryName(GetCurrentFilePath()) ?? "";
        }

        [Fact]
        public void FileEmbedTest()
        {
            var original = File.ReadAllBytes(Path.Combine(currentFolder, "./a.txt"));
            Assert.True(GetA().SequenceEqual(original.AsSpan()));
        }

        [Fact]
        public void FileNotFoundTest()
        {
            Assert.Throws<FileNotFoundException>(() => GetB("b.cs"));
        }

        [Fact]
        public void FolderFileEmbedTest()
        {
            const string path = "EmbedResourceCSharp/Generator.cs";
            var original = File.ReadAllBytes(Path.Combine(currentFolder, "../../src/" + path));
            Assert.True(GetB(path).SequenceEqual(original.AsSpan()));
        }

        [FolderEmbed("../../src", "*.cs")]
        private static partial ReadOnlySpan<byte> GetB(ReadOnlySpan<char> s);
    }
}
