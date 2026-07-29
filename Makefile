# SlopClean — common developer targets
# Requires: .NET 10 SDK, GNU Make (e.g. Chocolatey `make`)
#
# Usage:
#   make help
#   make run
#   make build
#   make ci
#   make release

.PHONY: help restore build test run ci release clean publish checksum zip

SOLUTION          := SlopClean.slnx
APP_PROJECT       := src/SlopClean.App/SlopClean.App.csproj
ELEVATED_PROJECT  := src/SlopClean.Elevated/SlopClean.Elevated.csproj
CONFIGURATION     ?= Release
RID               ?= win-x64
APP_PLATFORM      ?= x64
ARTIFACTS_DIR     := artifacts
PUBLISH_DIR       := $(ARTIFACTS_DIR)/slopclean-$(RID)
ZIP_PATH          := $(ARTIFACTS_DIR)/slopclean-$(RID).zip
SHA_PATH          := $(ARTIFACTS_DIR)/slopclean-$(RID).sha256.txt
TEST_RESULTS      := TestResults
APP_PLATFORM_ARGS := -p:Platform=$(APP_PLATFORM)

ifeq ($(OS),Windows_NT)
  SHELL := cmd.exe
  .SHELLFLAGS := /C
  MKDIR_P = if not exist "$(subst /,\,$(1))" mkdir "$(subst /,\,$(1))"
  RM_RF   = if exist "$(subst /,\,$(1))" rmdir /S /Q "$(subst /,\,$(1))"
else
  MKDIR_P = mkdir -p $(1)
  RM_RF   = rm -rf $(1)
endif

help:
	@echo SlopClean Make targets
	@echo.
	@echo   make restore   Restore NuGet packages
	@echo   make build     Build Elevated helper + full solution (Release)
	@echo   make test      Run all tests (Release)
	@echo   make run       Launch the WinUI app (Debug)
	@echo   make ci        Restore, build, test (CI parity)
	@echo   make release   Self-contained publish, SHA-256, ZIP
	@echo   make clean     Remove bin/obj/artifacts/TestResults
	@echo.
	@echo Variables: CONFIGURATION=$(CONFIGURATION) RID=$(RID) APP_PLATFORM=$(APP_PLATFORM)

restore:
	dotnet restore $(SOLUTION)

# Elevated first so the App copy-helper target can pick up the exe.
build: restore
	dotnet build $(ELEVATED_PROJECT) -c $(CONFIGURATION) $(APP_PLATFORM_ARGS) --no-restore
	dotnet build $(APP_PROJECT) -c $(CONFIGURATION) $(APP_PLATFORM_ARGS) --no-restore
	dotnet build $(SOLUTION) -c $(CONFIGURATION) --no-restore

test: build
	dotnet test $(SOLUTION) -c $(CONFIGURATION) --no-build --logger trx --results-directory $(TEST_RESULTS)

run:
	dotnet build $(ELEVATED_PROJECT) -c Debug $(APP_PLATFORM_ARGS)
	dotnet run --project $(APP_PROJECT) -c Debug $(APP_PLATFORM_ARGS) --no-launch-profile

ci: restore
	dotnet build $(ELEVATED_PROJECT) -c $(CONFIGURATION) $(APP_PLATFORM_ARGS) --no-restore
	dotnet build $(APP_PROJECT) -c $(CONFIGURATION) $(APP_PLATFORM_ARGS) --no-restore
	dotnet build $(SOLUTION) -c $(CONFIGURATION) --no-restore
	dotnet test $(SOLUTION) -c $(CONFIGURATION) --no-build --logger trx --results-directory $(TEST_RESULTS)

publish: build
	@$(call MKDIR_P,$(ARTIFACTS_DIR))
	dotnet publish $(APP_PROJECT) -c $(CONFIGURATION) -r $(RID) --self-contained true $(APP_PLATFORM_ARGS) -o $(PUBLISH_DIR)

checksum: publish
ifeq ($(OS),Windows_NT)
	powershell -NoProfile -Command "$$h = Get-FileHash '$(PUBLISH_DIR)/SlopClean.App.exe' -Algorithm SHA256; Set-Content -Path '$(SHA_PATH)' -Value ('SHA256 ' + $$h.Hash + '  SlopClean.App.exe')"
else
	cd $(PUBLISH_DIR) && sha256sum SlopClean.App.exe > ../slopclean-$(RID).sha256.txt
endif

zip: checksum
ifeq ($(OS),Windows_NT)
	powershell -NoProfile -Command "if (Test-Path '$(ZIP_PATH)') { Remove-Item -Force '$(ZIP_PATH)' }; Compress-Archive -Path '$(PUBLISH_DIR)\*' -DestinationPath '$(ZIP_PATH)'"
else
	rm -f $(ZIP_PATH)
	cd $(ARTIFACTS_DIR) && zip -r slopclean-$(RID).zip slopclean-$(RID) slopclean-$(RID).sha256.txt
endif

release: zip
	@echo.
	@echo Release artifacts:
	@echo   $(PUBLISH_DIR)/
	@echo   $(SHA_PATH)
	@echo   $(ZIP_PATH)

clean:
	-$(call RM_RF,src/SlopClean.App/bin)
	-$(call RM_RF,src/SlopClean.App/obj)
	-$(call RM_RF,src/SlopClean.Core/bin)
	-$(call RM_RF,src/SlopClean.Core/obj)
	-$(call RM_RF,src/SlopClean.Modules/bin)
	-$(call RM_RF,src/SlopClean.Modules/obj)
	-$(call RM_RF,src/SlopClean.Modules.TempCleaner/bin)
	-$(call RM_RF,src/SlopClean.Modules.TempCleaner/obj)
	-$(call RM_RF,src/SlopClean.Modules.RecycleBin/bin)
	-$(call RM_RF,src/SlopClean.Modules.RecycleBin/obj)
	-$(call RM_RF,src/SlopClean.Modules.BrowserCleaner/bin)
	-$(call RM_RF,src/SlopClean.Modules.BrowserCleaner/obj)
	-$(call RM_RF,src/SlopClean.Modules.StartupManager/bin)
	-$(call RM_RF,src/SlopClean.Modules.StartupManager/obj)
	-$(call RM_RF,src/SlopClean.Modules.DiskAnalyzer/bin)
	-$(call RM_RF,src/SlopClean.Modules.DiskAnalyzer/obj)
	-$(call RM_RF,src/SlopClean.Modules.UninstallCleanup/bin)
	-$(call RM_RF,src/SlopClean.Modules.UninstallCleanup/obj)
	-$(call RM_RF,src/SlopClean.Modules.ServiceAdvisor/bin)
	-$(call RM_RF,src/SlopClean.Modules.ServiceAdvisor/obj)
	-$(call RM_RF,src/SlopClean.Modules.CoreIsolationDrivers/bin)
	-$(call RM_RF,src/SlopClean.Modules.CoreIsolationDrivers/obj)
	-$(call RM_RF,src/SlopClean.Platform.Windows/bin)
	-$(call RM_RF,src/SlopClean.Platform.Windows/obj)
	-$(call RM_RF,src/SlopClean.Elevated/bin)
	-$(call RM_RF,src/SlopClean.Elevated/obj)
	-$(call RM_RF,tests/SlopClean.Core.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Core.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.TestSupport/bin)
	-$(call RM_RF,tests/SlopClean.Modules.TestSupport/obj)
	-$(call RM_RF,tests/SlopClean.Modules.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.TempCleaner.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.TempCleaner.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.RecycleBin.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.RecycleBin.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.BrowserCleaner.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.BrowserCleaner.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.StartupManager.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.StartupManager.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.DiskAnalyzer.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.DiskAnalyzer.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.UninstallCleanup.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.UninstallCleanup.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.ServiceAdvisor.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.ServiceAdvisor.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Modules.CoreIsolationDrivers.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Modules.CoreIsolationDrivers.Tests/obj)
	-$(call RM_RF,tests/SlopClean.Platform.Windows.Tests/bin)
	-$(call RM_RF,tests/SlopClean.Platform.Windows.Tests/obj)
	-$(call RM_RF,$(ARTIFACTS_DIR))
	-$(call RM_RF,$(TEST_RESULTS))
