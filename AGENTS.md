# Repository instructions

## Git publishing

- When asked to commit, push, or create a tag, use the `git` CLI directly.
- Do not block direct Git operations on `gh auth status`: the Codex task isolation can make
  the GitHub CLI keyring appear unavailable even when the repository's Git credentials work.
- Use `gh` only when the requested operation specifically requires GitHub features that plain
  Git does not provide, such as creating or reviewing a pull request.
