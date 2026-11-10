# Weapon retirement checks

Run `dotnet run --project tests/WeaponRetirementTests/WeaponRetirementTests.csproj -c Release`.

Links the production retirement helper. The engine boundary deliberately drops ActiveWeapon regardless of the passed wrapper. Checks keep unrelated held guns owned and selected, delete only the intended victim, handle all retirement orders, and preserve attached items on failure. This adapter cannot prove native behavior or client rendering; complete full-refresh native and human-client acceptance separately.
