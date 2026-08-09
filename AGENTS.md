# Repository instructions

## Git publishing

- When asked to commit, push, or create a tag, use the `git` CLI directly.
- Do not block direct Git operations on `gh auth status`: the Codex task isolation can make
  the GitHub CLI keyring appear unavailable even when the repository's Git credentials work.
- Use `gh` only when the requested operation specifically requires GitHub features that plain
  Git does not provide, such as creating or reviewing a pull request.

## Testing

- Default command: `dotnet test --filter "Category!=Integration"` (excludes network tests).
- Integration tests require a valid Ollama Web Search API key in the DPAPI secret store.
- To run integration tests: `dotnet test --filter "Category=Integration"` (user must request explicitly).
