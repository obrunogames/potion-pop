#!/usr/bin/env bash
# Fast standalone C# compile check (no Unity Editor needed, safe to run in parallel).
#
#   Tools/compile_check.sh            # runtime (player + editor defines), editor and tests assemblies
#   Tools/compile_check.sh runtime    # only the runtime assembly
#
# Uses Unity's bundled Roslyn compiler with the engine/package assemblies as references. It mirrors the asmdefs:
#   Assets/_Game/Scripts  -> PotionPop.Runtime  (defines SP_ADMOB / SP_APPLE_AUTH like the asmdef version defines)
#   Assets/_Game/Editor   -> PotionPop.Editor
#   Assets/_Game/Tests    -> PotionPop.Tests (EditMode)
# Requires Library/ to exist (open the project once in Unity or run it in batch mode).
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
S=/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources/Scripting
DOTNET="$S/DotNetSdk/dotnet"
CSC="$(ls -d "$S"/DotNetSdk/sdk/*/Roslyn/bincore/csc.dll | head -1)"
API="$S/UnityReferenceAssemblies/unity-4.8-api"
LIB="$ROOT/Library"
# Until this project has been opened in Unity once, borrow the (same Unity version + packages) Library of Shelf Pop.
if [ ! -d "$LIB/ScriptAssemblies" ] || [ ! -f "$LIB/ScriptAssemblies/UnityEngine.UI.dll" ]; then
  LIB="$ROOT/../shelf-sorting/Library"
fi
SA="$LIB/ScriptAssemblies"
PC="$LIB/PackageCache"
OUT="${TMPDIR:-/tmp}/potionpop_compile_$$"
mkdir -p "$OUT"
trap 'rm -rf "$OUT"' EXIT

WHAT="${1:-all}"
export DOTNET_CLI_UI_LANGUAGE=en
WARN="-nowarn:0169,0414,0649,0067,0618,1998,0219,0162,0168,8632"
BASE_DEFS="UNITY_6000_0_OR_NEWER;UNITY_2021_3_OR_NEWER;UNITY_2022_3_OR_NEWER;UNITY_2023_1_OR_NEWER;UNITY_5_3_OR_NEWER;CSHARP_7_3_OR_NEWER;ENABLE_INPUT_SYSTEM;SP_ADMOB;SP_APPLE_AUTH;TEXTMESHPRO_PRESENT"

refs_common() {
  for f in "$API"/mscorlib.dll "$API"/System.dll "$API"/System.Core.dll "$API"/System.Xml.dll "$API"/System.Xml.Linq.dll \
           "$API"/System.Runtime.Serialization.dll "$API"/System.Net.Http.dll "$API"/System.Numerics.dll "$API"/Facades/*.dll; do
    echo "-r:$f"
  done
  for f in "$S"/Managed/UnityEngine/UnityEngine*.dll; do echo "-r:$f"; done
  for n in UnityEngine.UI Unity.TextMeshPro Unity.InputSystem AppleAuth; do
    [ -f "$SA/$n.dll" ] && echo "-r:$SA/$n.dll"
  done
  for f in "$PC"/com.google.ads.mobile@*/GoogleMobileAds/GoogleMobileAds*.dll; do
    case "$f" in *Android*|*iOS*|*Unity.dll) ;; *) echo "-r:$f";; esac
  done
}

refs_editor() {
  for f in "$S"/Managed/UnityEngine/UnityEditor*.dll; do echo "-r:$f"; done
  for n in UnityEditor.UI Unity.TextMeshPro.Editor; do
    [ -f "$SA/$n.dll" ] && echo "-r:$SA/$n.dll"
  done
}

csc() { # out defines sources-file extra-ref-args...
  local out="$1" defs="$2" srcs="$3"; shift 3
  "$DOTNET" "$CSC" -nologo -noconfig -preferreduilang:en -nostdlib+ -langversion:9.0 -target:library -unsafe- -deterministic \
    $WARN -define:"$defs" -out:"$out" $(refs_common) "$@" @"$srcs" 2>&1 \
    | grep -E "error|warning CS0(108|114|252|253|472|649|665|693|1717)" | sed "s|$ROOT/||" | sort -u
  return ${PIPESTATUS[0]}
}

status=0
find Assets/_Game/Scripts -name "*.cs" > "$OUT/runtime.txt"
if [ -s "$OUT/runtime.txt" ]; then
  echo "== runtime (player: Android)"
  csc "$OUT/PotionPop.Runtime.Player.dll" "$BASE_DEFS;UNITY_ANDROID" "$OUT/runtime.txt" || status=1
  echo "== runtime (player: iOS)"
  csc "$OUT/PotionPop.Runtime.iOS.dll" "$BASE_DEFS;UNITY_IOS" "$OUT/runtime.txt" || status=1
  echo "== runtime (editor)"
  csc "$OUT/PotionPop.Runtime.dll" "$BASE_DEFS;UNITY_EDITOR;UNITY_EDITOR_OSX" "$OUT/runtime.txt" $(refs_editor) || status=1
fi
[ "$WHAT" = "runtime" ] && exit $status

if [ -d Assets/_Game/Editor ] && [ -f "$OUT/PotionPop.Runtime.dll" ]; then
  find Assets/_Game/Editor -name "*.cs" > "$OUT/editor.txt"
  if [ -s "$OUT/editor.txt" ]; then
    echo "== editor"
    csc "$OUT/PotionPop.Editor.dll" "$BASE_DEFS;UNITY_EDITOR;UNITY_EDITOR_OSX" "$OUT/editor.txt" $(refs_editor) \
      -r:"$OUT/PotionPop.Runtime.dll" || status=1
    ANDROID_EDITOR=/Applications/Unity/Hub/Editor/6000.6.3f1/PlaybackEngines/AndroidPlayer/UnityEditor.Android.Extensions.dll
    if [ -f "$ANDROID_EDITOR" ]; then
      echo "== editor (Android build processors)"
      csc "$OUT/PotionPop.Editor.Android.dll" "$BASE_DEFS;UNITY_EDITOR;UNITY_EDITOR_OSX;UNITY_ANDROID" "$OUT/editor.txt" $(refs_editor) \
        -r:"$OUT/PotionPop.Runtime.dll" -r:"$ANDROID_EDITOR" || status=1
    fi
  fi
fi

if [ -d Assets/_Game/Tests ] && [ -f "$OUT/PotionPop.Runtime.dll" ]; then
  find Assets/_Game/Tests -name "*.cs" > "$OUT/tests.txt"
  if [ -s "$OUT/tests.txt" ]; then
    echo "== tests"
    NUNIT="$(ls "$PC"/com.unity.ext.nunit@*/net472/unity-custom/nunit.framework.dll | head -1)"
    csc "$OUT/PotionPop.Tests.dll" "$BASE_DEFS;UNITY_EDITOR;UNITY_INCLUDE_TESTS" "$OUT/tests.txt" $(refs_editor) \
      -r:"$OUT/PotionPop.Runtime.dll" -r:"$NUNIT" -r:"$SA/UnityEngine.TestRunner.dll" -r:"$SA/UnityEditor.TestRunner.dll" || status=1
  fi
fi

[ $status -eq 0 ] && echo "COMPILE OK" || echo "COMPILE FAILED"
exit $status
