namespace PortainerToArcane.Tests;

public sealed class ComposeAnalyzerTests
{
    [Fact]
    public void Analyze_DetectsHighRiskDockerSocket()
    {
        const string compose = """
            services:
              agent:
                image: example/agent:latest
                network_mode: host
                volumes:
                  - /var/run/docker.sock:/var/run/docker.sock
            """;
        var stack = new StackInfo(1, "agent", 2, 3, 1, "docker-compose.yml", [], compose, null, "Editor/Upload");

        var result = new ComposeAnalyzer().Analyze(stack);

        Assert.Equal(MigrationRisk.High, result.Risk);
        Assert.Contains(result.Findings, finding => finding.Code == "docker.socket");
        Assert.Contains(result.Findings, finding => finding.Code == "network.host");
    }

    [Fact]
    public void Analyze_StatelessPinnedImage_IsEasy()
    {
        const string compose = """
            services:
              web:
                image: nginx:1.29.1
            """;
        var stack = new StackInfo(1, "web", 2, 3, 1, "docker-compose.yml", [], compose, null, "Editor/Upload");

        var result = new ComposeAnalyzer().Analyze(stack);

        Assert.Equal(MigrationRisk.Easy, result.Risk);
    }
}
