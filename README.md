<img src="./docs/icon.png" align="left" height="128px" alt="two strawberries, getting merged together">

# Celeste Savefile Merger

<br clear="left" />
<br />


## Usage

![select screen](./docs/select.png)
![merge screen](./docs/merge.png)
![save screen](./docs/save.png)

## Development

**Build Program**

```sh
dotnet publish -o out
```

**Build Installer**

```sh
dotnet publish SaveMerger
dotnet build MsiPackage --configuration Release
ls MsiPackage/bin/Release/en-US/CelesteSaveMerger.msi
```
