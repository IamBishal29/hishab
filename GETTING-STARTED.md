# Getting started with the Hishab agent kit

Run these in PowerShell. Delete this file in your first PR.

## 1. Check the tools
```powershell
dotnet --version     # 10.x  (install the .NET 10 SDK if missing)
node -v              # LTS
pnpm -v              # npm i -g pnpm
docker version
gh auth status       # gh auth login if needed
```

## 2. Make the folder a git repo and create the GitHub repo in one go
No need to create the repo on the GitHub website first.
```powershell
cd D:\Practice\hishab
git init -b main
git config user.name  "Syed Ahasan Habib"
git config user.email "<the email you use for GitHub>"
git add .
git commit -m "chore: add agent kit and PRD"
gh repo create hishab --public --source . --remote origin --push
```
This is the only commit that goes straight to `main`. From now on everything
goes through branches and PRs.

## 3. Start
```powershell
claude
```
Then type `/kickoff`. After that, each session is `/next-task`, and `/wrap-up`
before you stop.
