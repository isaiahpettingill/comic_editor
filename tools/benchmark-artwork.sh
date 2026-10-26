#!/usr/bin/env bash
# Compare the complete historical model/codec with the current implementation.
# Requires the repo's .NET SDK. No SDK or system tools are installed here; normal NuGet restore may run.
set -euo pipefail
repo=$(git rev-parse --show-toplevel)
baseline=${1:-d8fe2115e1d264ca96052d61e1d0d56742ba8b5b}
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
mkdir -p "$scratch/original"
while IFS= read -r path; do
    [[ "$path" == ComicEditor.Format/*.cs && "${path#ComicEditor.Format/}" != */* ]] || continue
    git -C "$repo" show "$baseline:$path" | sed 's/namespace ComicEditor.Format;/namespace BaselineComicEditor.Format;/' > "$scratch/original/$(basename "$path")"
done < <(git -C "$repo" ls-tree -r --name-only "$baseline" ComicEditor.Format)
cp "$repo/tools/ArtworkBenchmark/Program.cs" "$scratch/Program.cs"
# Use a ProjectReference so protobuf and other format dependencies are identical.
cat > "$scratch/benchmark.csproj" <<PROJECT
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup><ProjectReference Include="$repo/ComicEditor.Format/ComicEditor.Format.csproj" /></ItemGroup>
</Project>
PROJECT
dotnet run --project "$scratch/benchmark.csproj" -c Debug -m:1 -p:UseSharedCompilation=false
