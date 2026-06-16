using System.Linq;
using System.Xml.Linq;
using KinetixModManager;
using Xunit;

namespace KinetixModManager.Tests;

/// <summary>
/// Covers the lenient <see cref="FomodParser"/>: element/attribute casing tolerance, the omitted-vs-empty
/// destination distinction, simple and conditional type descriptors, and conditionalFileInstalls.
/// </summary>
public class FomodParserTests
{
    private static FomodConfig Parse(string xml) => FomodParser.Parse(XDocument.Parse(xml));

    [Fact]
    public void Parses_ModuleName_Steps_Groups_Plugins_AndFiles()
    {
        var config = Parse(@"
<config>
  <moduleName>My Mod</moduleName>
  <installSteps>
    <installStep name='Step One'>
      <optionalFileGroups>
        <group name='Pick One' type='SelectExactlyOne'>
          <plugins>
            <plugin name='Option A'>
              <description>  desc A  </description>
              <files>
                <file source='data\a.esp' destination='a.esp' priority='5'/>
                <folder source='tex' destination='textures'/>
              </files>
            </plugin>
          </plugins>
        </group>
      </optionalFileGroups>
    </installStep>
  </installSteps>
</config>");

        Assert.Equal("My Mod", config.ModuleName);
        FomodInstallStep step = Assert.Single(config.InstallSteps);
        Assert.Equal("Step One", step.Name);
        FomodGroup group = Assert.Single(step.Groups);
        Assert.Equal(FomodGroupType.SelectExactlyOne, group.Type);
        FomodPlugin plugin = Assert.Single(group.Plugins);
        Assert.Equal("Option A", plugin.Name);
        Assert.Equal("desc A", plugin.Description); // trimmed

        Assert.Equal(2, plugin.Files.Count);
        FomodFileItem file = plugin.Files[0];
        Assert.Equal("data\\a.esp", file.Source);
        Assert.Equal("a.esp", file.Destination);
        Assert.Equal(5, file.Priority);
        Assert.False(file.IsFolder);
        Assert.True(plugin.Files[1].IsFolder);
    }

    [Fact]
    public void Distinguishes_OmittedDestination_From_EmptyDestination()
    {
        var config = Parse(@"
<config>
  <requiredInstallFiles>
    <file source='a.esp'/>
    <file source='b.esp' destination=''/>
  </requiredInstallFiles>
</config>");

        Assert.Null(config.RequiredInstallFiles[0].Destination);   // omitted -> mirror source
        Assert.Equal("", config.RequiredInstallFiles[1].Destination); // explicit Data root
    }

    [Fact]
    public void Parses_ConditionFlags_AndSimpleTypeDescriptor()
    {
        var config = Parse(@"
<config>
  <installSteps>
    <installStep name='s'>
      <optionalFileGroups>
        <group name='g' type='SelectAny'>
          <plugins>
            <plugin name='p'>
              <conditionFlags>
                <flag name='picked'>yes</flag>
              </conditionFlags>
              <typeDescriptor>
                <type name='Recommended'/>
              </typeDescriptor>
            </plugin>
          </plugins>
        </group>
      </optionalFileGroups>
    </installStep>
  </installSteps>
</config>");

        FomodPlugin plugin = config.InstallSteps[0].Groups[0].Plugins[0];
        FomodFlag flag = Assert.Single(plugin.ConditionFlags);
        Assert.Equal("picked", flag.Name);
        Assert.Equal("yes", flag.Value);
        Assert.Equal(FomodPluginType.Recommended, plugin.TypeDescriptor.DefaultType);
        Assert.Empty(plugin.TypeDescriptor.Patterns);
    }

    [Fact]
    public void Parses_DependencyTypeDescriptor_WithPatterns()
    {
        var config = Parse(@"
<config>
  <installSteps>
    <installStep name='s'>
      <optionalFileGroups>
        <group name='g' type='SelectAny'>
          <plugins>
            <plugin name='p'>
              <typeDescriptor>
                <dependencyType>
                  <defaultType name='Optional'/>
                  <patterns>
                    <pattern>
                      <dependencies operator='And'>
                        <fileDependency file='Skyrim.esm' state='Active'/>
                      </dependencies>
                      <type name='Recommended'/>
                    </pattern>
                  </patterns>
                </dependencyType>
              </typeDescriptor>
            </plugin>
          </plugins>
        </group>
      </optionalFileGroups>
    </installStep>
  </installSteps>
</config>");

        FomodTypeDescriptor desc = config.InstallSteps[0].Groups[0].Plugins[0].TypeDescriptor;
        Assert.Equal(FomodPluginType.Optional, desc.DefaultType);
        FomodTypePattern pattern = Assert.Single(desc.Patterns);
        Assert.Equal(FomodPluginType.Recommended, pattern.Type);
        Assert.NotNull(pattern.Dependencies);
        FomodFileDependency fileDep = Assert.Single(pattern.Dependencies!.FileDependencies);
        Assert.Equal("Skyrim.esm", fileDep.File);
        Assert.Equal(FomodFileState.Active, fileDep.State);
    }

    [Fact]
    public void Parses_StepVisible_And_ConditionalFileInstalls()
    {
        var config = Parse(@"
<config>
  <installSteps>
    <installStep name='gated'>
      <visible operator='And'>
        <flagDependency flag='show' value='on'/>
      </visible>
    </installStep>
  </installSteps>
  <conditionalFileInstalls>
    <patterns>
      <pattern>
        <dependencies operator='Or'>
          <flagDependency flag='a' value='1'/>
          <flagDependency flag='b' value='1'/>
        </dependencies>
        <files>
          <file source='extra.esp' destination='extra.esp'/>
        </files>
      </pattern>
    </patterns>
  </conditionalFileInstalls>
</config>");

        Assert.NotNull(config.InstallSteps[0].Visible);
        Assert.Equal("show", config.InstallSteps[0].Visible!.FlagDependencies[0].Name);

        FomodConditionalInstall ci = Assert.Single(config.ConditionalInstalls);
        Assert.Equal(FomodDependencyOperator.Or, ci.Dependencies!.Operator);
        Assert.Equal(2, ci.Dependencies!.FlagDependencies.Count);
        Assert.Equal("extra.esp", Assert.Single(ci.Files).Source);
    }

    [Fact]
    public void IsTolerantOf_ElementCasing()
    {
        // Real FOMODs vary casing freely; the parser matches local names case-insensitively.
        var config = Parse(@"
<CONFIG>
  <ModuleName>Cased</ModuleName>
  <InstallSteps>
    <InstallStep name='s'>
      <OptionalFileGroups>
        <Group name='g' type='SelectAll'>
          <Plugins><Plugin name='p'/></Plugins>
        </Group>
      </OptionalFileGroups>
    </InstallStep>
  </InstallSteps>
</CONFIG>");

        Assert.Equal("Cased", config.ModuleName);
        Assert.Equal(FomodGroupType.SelectAll, config.InstallSteps[0].Groups[0].Type);
        Assert.Equal("p", config.InstallSteps[0].Groups[0].Plugins[0].Name);
    }
}
