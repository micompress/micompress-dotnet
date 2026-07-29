# micompress (.NET tool)

> Compress **images, PDFs, video, audio, documents & archives** from the command line.
> Powered by [**micompress.com**](https://micompress.com/?utm_source=nuget&utm_medium=cli&utm_campaign=extend).

```bash
dotnet tool install -g micompress
micompress photo.jpg
```

## Usage

```bash
micompress *.png --preset extreme
micompress report.pdf -o out/
```

Options: `-o/--output`, `-p/--preset` (`lossless|recommended|extreme`), `--token`, `--api`, `-q/--quiet`.

## Build & publish

Requires the .NET SDK (not available on this build server):

```bash
dotnet pack -c Release
dotnet nuget push bin/Release/micompress.0.1.0.nupkg -k <API_KEY> -s https://api.nuget.org/v3/index.json
```

## License

MIT © [MiCompress](https://micompress.com)
