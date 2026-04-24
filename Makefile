TFM      := netstandard2.1
CONFIG   := Debug
DLL      := VGAnima.dll

BUILDDIR := VGAnima/bin/$(CONFIG)/$(TFM)
BUILDDLL := $(BUILDDIR)/$(DLL)

# WSL path to the game install — adjust if Steam lives elsewhere.
GAME_DIR := /mnt/c/Program Files (x86)/Steam/steamapps/common/Vanguard Galaxy
PLUGIN_DIR := $(GAME_DIR)/BepInEx/plugins
VGANIMA_DIR := $(PLUGIN_DIR)/VGAnima

# Path to the sibling VGTTS checkout — we reuse its publicized stub.
VGTTS_LIB := ../vanguard-galaxy/VGTTS/lib

# Path to the sibling VGMissionJournal checkout — we reference its released DLL
# as a typed soft-dep (runtime load is handled by BepInEx independently).
VGMISSIONJOURNAL_DLL := ../vanguard-galaxy-missionjournal/VGMissionJournal/bin/Release/netstandard2.1/VGMissionJournal.dll

DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo /tmp/dnsdk/dotnet/dotnet)

.PHONY: all build link-asm link-missionjournal link-libs deploy clean test

all: build

# Symlink the VGTTS-maintained publicized Assembly-CSharp.dll into VGAnima/lib/
# so we compile against the same stub (single source of truth).
link-asm:
	@mkdir -p VGAnima/lib
	@if [ ! -e "VGAnima/lib/Assembly-CSharp.dll" ]; then \
		ln -sf "$(abspath $(VGTTS_LIB))/Assembly-CSharp.dll" VGAnima/lib/Assembly-CSharp.dll ; \
		echo "Linked Assembly-CSharp.dll from $(VGTTS_LIB)" ; \
	fi

# Symlink the latest Release-built VGMissionJournal.dll into VGAnima/lib/. Typed
# reference only — the game loads VGMissionJournal as its own plugin at runtime.
# Re-runs each build so the link picks up API updates as the sibling rebuilds.
link-missionjournal:
	@mkdir -p VGAnima/lib
	@ln -sf "$(abspath $(VGMISSIONJOURNAL_DLL))" VGAnima/lib/VGMissionJournal.dll

link-libs: link-asm link-missionjournal

build: link-libs
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) build VGAnima/VGAnima.csproj -c $(CONFIG)

test:
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) test VGAnima.Tests/VGAnima.Tests.csproj -c $(CONFIG)

deploy: build
	@test -d "$(PLUGIN_DIR)" || { echo "BepInEx plugins dir not found at $(PLUGIN_DIR)" ; exit 1 ; }
	@mkdir -p "$(VGANIMA_DIR)"
	# Copy every runtime assembly from bin/. CopyLocalLockFileAssemblies=true in
	# VGAnima.csproj restricts bin/ to VGAnima.dll + NuGet runtime deps that the
	# game doesn't ship (System.Text.Json + its transitive deps); BepInEx / Harmony /
	# UnityEngine / Newtonsoft are compile-only so they don't land here.
	cp "$(BUILDDIR)"/*.dll "$(VGANIMA_DIR)/"
	@if [ -f "$(BUILDDIR)/VGAnima.pdb" ]; then cp "$(BUILDDIR)/VGAnima.pdb" "$(VGANIMA_DIR)/"; fi
	@echo "Deployed $(shell ls $(BUILDDIR)/*.dll | wc -l) DLL(s) to $(VGANIMA_DIR)"

clean:
	-$(DOTNET) clean VGAnima/VGAnima.csproj
	rm -rf VGAnima/bin VGAnima/obj VGAnima.Tests/bin VGAnima.Tests/obj dist/
