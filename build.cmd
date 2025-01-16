@echo off

set TARGET_DIR=out
set ZIP_FILE=AchievementsBooster.zip

if exist "%TARGET_DIR%" (
  rmdir /s /q "%TARGET_DIR%"
)

dotnet publish AchievementsBooster -c "Release" -o "%TARGET_DIR%"

copy README.md "%TARGET_DIR%"
copy NOTICE.md "%TARGET_DIR%"

pushd "%TARGET_DIR%"
tar -a -c -f "../%ZIP_FILE%" *

popd
move "%ZIP_FILE%" "%TARGET_DIR%"
