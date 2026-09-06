TFM      := netstandard2.1
CONFIG   := Debug
DLL      := VGAnima.dll

BUILDDIR := VGAnima/bin/$(CONFIG)/$(TFM)
BUILDDLL := $(BUILDDIR)/$(DLL)

# WSL path to the game install — adjust if Steam lives elsewhere.
GAME_DIR ?= /mnt/c/Program Files (x86)/Steam/steamapps/common/Vanguard Galaxy
PLUGIN_DIR := $(GAME_DIR)/BepInEx/plugins
VGANIMA_DIR := $(PLUGIN_DIR)/VGAnima

# Owner-local reference for inspected game 0.8.2.3. Never distribute game DLLs.
PUBLICIZER ?= assembly-publicizer
GAME_ASSEMBLY_SHA256 := a2aad60bc68c31baccd636587d3c5ba4e651eacda59b0af42cd4f17f864284fb

# Path to the sibling VGMissionJournal checkout — we reference its released DLL
# as a typed soft-dep (runtime load is handled by BepInEx independently).
VGMISSIONJOURNAL_DLL := ../vanguard-galaxy-missionjournal/VGMissionJournal/bin/Release/netstandard2.1/VGMissionJournal.dll

VGAPI_DLL ?= ../vanguard-galaxy-api/VGModAPI.Abstractions/bin/Release/netstandard2.1/VGModAPI.Abstractions.dll

DOTNET ?= $(shell command -v dotnet 2>/dev/null || echo /tmp/dnsdk/dotnet/dotnet)

.PHONY: all build link-asm refresh-asm refresh-test-asm link-test-asm check-asm-source link-missionjournal link-api link-libs deploy clean test

all: build

check-asm-source:
	@test "$$(sha256sum "$(GAME_DIR)/VanguardGalaxy_Data/Managed/Assembly-CSharp.dll" | cut -d' ' -f1)" = "$(GAME_ASSEMBLY_SHA256)" || { echo 'Unsupported game assembly; re-inspect before building.'; exit 1; }

refresh-asm: check-asm-source
	mkdir -p .local-reference
	DOTNET_ROLL_FORWARD=LatestMajor $(PUBLICIZER) --strip "$(GAME_DIR)/VanguardGalaxy_Data/Managed/Assembly-CSharp.dll" -o .local-reference/
	@test -s .local-reference/Assembly-CSharp-publicized.dll
	@printf '%s' '$(GAME_ASSEMBLY_SHA256)' > .local-reference/source.sha256
	@sha256sum .local-reference/Assembly-CSharp-publicized.dll > .local-reference/reference.sha256

link-asm:
	@if [ -f "$(GAME_DIR)/VanguardGalaxy_Data/Managed/Assembly-CSharp.dll" ]; then $(MAKE) check-asm-source; fi
	@test "$$(cat .local-reference/source.sha256 2>/dev/null)" = "$(GAME_ASSEMBLY_SHA256)" && sha256sum --status -c .local-reference/reference.sha256 || { echo 'Run make refresh-asm using the installed game and assembly-publicizer.'; exit 1; }
	@mkdir -p VGAnima/lib
	ln -sfn "$(CURDIR)/.local-reference/Assembly-CSharp-publicized.dll" VGAnima/lib/Assembly-CSharp.dll

# Symlink the latest Release-built VGMissionJournal.dll into VGAnima/lib/. Typed
# reference only — the game loads VGMissionJournal as its own plugin at runtime.
# Re-runs each build so the link picks up API updates as the sibling rebuilds.
link-missionjournal:
	@mkdir -p VGAnima/lib
	@ln -sf "$(abspath $(VGMISSIONJOURNAL_DLL))" VGAnima/lib/VGMissionJournal.dll

link-api:
	@test -s "$(VGAPI_DLL)" || { echo 'Build VGModAPI Release first or set VGAPI_DLL.'; exit 1; }
	@mkdir -p VGAnima/lib
	ln -sfn "$(abspath $(VGAPI_DLL))" VGAnima/lib/VGModAPI.Abstractions.dll

link-libs: link-asm link-missionjournal link-api

build: link-libs
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) build VGAnima/VGAnima.csproj -c $(CONFIG)

# Host tests need managed constructors/getters, not the throw-only compile stub.
refresh-test-asm: check-asm-source
	mkdir -p .local-test-reference
	DOTNET_ROLL_FORWARD=LatestMajor $(PUBLICIZER) "$(GAME_DIR)/VanguardGalaxy_Data/Managed/Assembly-CSharp.dll" -o .local-test-reference/
	$(MAKE) check-asm-source
	@printf '%s' '$(GAME_ASSEMBLY_SHA256)' > .local-test-reference/source.sha256
	@sha256sum .local-test-reference/Assembly-CSharp-publicized.dll > .local-test-reference/reference.sha256

link-test-asm:
	@test "$$(cat .local-test-reference/source.sha256 2>/dev/null)" = "$(GAME_ASSEMBLY_SHA256)" && sha256sum --status -c .local-test-reference/reference.sha256 || { echo 'Run make refresh-test-asm to generate the private host-test runtime reference.'; exit 1; }
	ln -sfn Assembly-CSharp-publicized.dll .local-test-reference/Assembly-CSharp.dll

test: link-libs link-test-asm
	DOTNET_ROOT=$(dir $(DOTNET)) $(DOTNET) test VGAnima.Tests/VGAnima.Tests.csproj -c $(CONFIG)

deploy: build
	@test -d "$(PLUGIN_DIR)" || { echo "BepInEx plugins dir not found at $(PLUGIN_DIR)" ; exit 1 ; }
	@mkdir -p "$(VGANIMA_DIR)"
	# Copy every runtime assembly from bin/. CopyLocalLockFileAssemblies=true in
	# VGAnima.csproj restricts bin/ to VGAnima.dll + NuGet runtime deps that the
	# game doesn't ship; BepInEx / Harmony /
	# UnityEngine / Newtonsoft are compile-only so they don't land here.
	cp "$(BUILDDIR)"/*.dll "$(VGANIMA_DIR)/"
	@if [ -f "$(BUILDDIR)/VGAnima.pdb" ]; then cp "$(BUILDDIR)/VGAnima.pdb" "$(VGANIMA_DIR)/"; fi
	@echo "Deployed $(shell ls $(BUILDDIR)/*.dll | wc -l) DLL(s) to $(VGANIMA_DIR)"

clean:
	-$(DOTNET) clean VGAnima/VGAnima.csproj
	rm -rf VGAnima/bin VGAnima/obj VGAnima.Tests/bin VGAnima.Tests/obj dist/
