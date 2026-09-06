#!/usr/bin/env bash
#
# 桌面端（Windows / Linux / macOS）发布脚本 —— 发布 Launcher 工程（含主程序捆绑目录）。
# 对每个平台单独发布，并将每个平台的产物压缩为：
#   ImmersingHomework-<版本>-<平台>.zip   （版本自动读取自 ImmersingHomework.csproj）
#
# ⚠️ 重要：不要对 ImmersingHomework.sln 带 -r 发布！解决方案里包含 Mobile(Android/iOS) 工程，
# 它们被按桌面宿主 RID 还原时会去拉已停产的 Mono.<宿主RID> 运行时包而报 NU1102
# （如 Microsoft.NETCore.App.Runtime.Mono.osx-arm64 / win-x64 / linux-x64，仅发布到 9.0.0-preview.7）。
# Android APK 请单独发布 ImmersingHomework.Mobile.Android.csproj；iOS 需在 macOS 单独处理。
#
# 用法：
#   ./scripts/publish-desktop.sh                           # Release、自包含（默认 true）
#   SELF_CONTAINED=false ./scripts/publish-desktop.sh      # 改为框架依赖
#   ./scripts/publish-desktop.sh Debug
#   RIDS="win-x64 linux-x64" ./scripts/publish-desktop.sh  # 只发布指定平台
#   OUT_DIR=./dist ./scripts/publish-desktop.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${1:-Release}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"
OUT_DIR="${OUT_DIR:-"$ROOT/artifacts/desktop"}"

# 默认平台（按需增删；可用环境变量 RIDS 覆盖）
DEFAULT_RIDS=("win-x64" "linux-x64" "osx-arm64")
if [[ -n "${RIDS:-}" ]]; then
    # shellcheck disable=SC2206
    RID_LIST=(${RIDS})
else
    RID_LIST=("${DEFAULT_RIDS[@]}")
fi

# 从主工程 csproj 读取版本号（AssemblyVersion），保证与文件名/产物一致
VERSION="$(sed -n 's/.*<AssemblyVersion>\([0-9][0-9.]*\)<\/AssemblyVersion>.*/\1/p' \
    "$ROOT/ImmersingHomework/ImmersingHomework.csproj" | head -n 1)"
if [[ -z "$VERSION" ]]; then
    echo "错误：无法从 ImmersingHomework.csproj 读取 AssemblyVersion" >&2
    exit 1
fi
echo "==> 版本: $VERSION"

# 将目录内容打包为 zip：优先 zip 命令，其次 python3，避免“zip 未安装”
pack_zip() {
    local src_dir="$1" zip_file="$2"
    rm -f "$zip_file"
    if command -v zip >/dev/null 2>&1; then
        (cd "$src_dir" && zip -qr "$zip_file" .)
    elif command -v python3 >/dev/null 2>&1; then
        python3 - "$src_dir" "$zip_file" <<'PY'
import shutil, sys
shutil.make_archive(sys.argv[2][:-4], "zip", sys.argv[1])
PY
    else
        echo "错误：打包 zip 需要 zip 或 python3 命令" >&2
        exit 1
    fi
}

for rid in "${RID_LIST[@]}"; do
    publish_dir="$OUT_DIR/$rid"
    zip_file="$OUT_DIR/ImmersingHomework-$VERSION-$rid.zip"

    echo "==> 发布 $rid (Config=$CONFIG, SelfContained=$SELF_CONTAINED)"
    dotnet publish "$ROOT/ImmersingHomework.Launcher/ImmersingHomework.Launcher.csproj" \
        -c "$CONFIG" \
        -r "$rid" \
        --self-contained "$SELF_CONTAINED" \
        -o "$publish_dir"

    echo "==> 压缩 $rid -> ${zip_file##*/}"
    pack_zip "$publish_dir" "$zip_file"
done

echo "完成，桌面端压缩包位于: $OUT_DIR/ImmersingHomework-$VERSION-*.zip"
