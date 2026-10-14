#!/bin/bash

# ソースコードをビルド
dotnet build -c Release

# パッケージをパブリッシュ
dotnet pack -c Release -o ./publish

# パブリッシュ
dotnet publish -c Release -o ./publish --no-build

echo "Publish completed successfully."