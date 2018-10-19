
using Sitecore.ContentTesting;

namespace Sitecore.Support.ContentTesting.Pipelines.ConvertToXConnectInteraction
{
  using System.Linq;
  using Sitecore.Analytics.XConnect.DataAccess.Pipelines.ConvertToXConnectInteractionPipeline;
  using Sitecore.ContentTesting.Model.xConnect;
  using Sitecore.XConnect.Collection.Model;
  using Sitecore.Globalization;
  using Sitecore.Analytics;
  using Sitecore.Data;
  using Sitecore.Analytics.Model;
  using Sitecore.ContentTesting.Model.Data.Items;
  using System;
  using Sitecore.ContentTesting.Models;

  public class ConvertPersonalizationEvent : ConvertToXConnectInteractionProcessorBase
  {
    public override void Process(ConvertToXConnectInteractionPipelineArgs args)
    {
      if (args != null)
      {
        var interaction = args.XConnectInteraction;
        var pageViewEvents = interaction.Events.OfType<PageViewEvent>().OrderByDescending(x => x.Timestamp).ToList();
        var pageDataList = args.TrackerVisitData.Pages;

        foreach (var pageData in pageDataList)
        {
          if (pageData?.PersonalizationData?.ExposedRules == null || !pageData.PersonalizationData.ExposedRules.Any())
          {
            continue;
          }

          var parentPage = pageViewEvents.Single(p => p.ItemId == pageData.Item.Id && p.Timestamp == pageData.DateTime && pageData.Item.Version == p.ItemVersion);

          TestSet testSet = null;
          TestCombination combination = null;
          var contentItem = Tracker.DefinitionDatabase?.GetItem(
              new ID(pageData.Item.Id),
              Language.Parse(pageData.Item.Language ?? Context.Language.Name),
              Sitecore.Data.Version.Parse(pageData.Item.Version));
          if (contentItem != null)
          {
            var testItem = Tracker.DefinitionDatabase.GetItem(new ID(pageData.MvTest.Id));
            if (testItem != null)
            {
              var testDefinitionItem = TestDefinitionItem.Create(testItem);
              if (testDefinitionItem != null)
              {
                var sitecoreDeviceId = pageData.SitecoreDevice != null ? pageData.SitecoreDevice.Id : Guid.Empty;
                var deviceId = new ID(sitecoreDeviceId);

                if (deviceId != ID.Null)
                {
                  var renderings = contentItem.Visualization.GetRenderings(Tracker.DefinitionDatabase.GetItem(deviceId), false);
                  var personalizationRenderings = renderings.Where(x => !string.IsNullOrEmpty(x.Settings.PersonalizationTest)).ToArray();
                  if (personalizationRenderings.Any())
                  {
                    testSet = TestManager.GetTestSet(new TestDefinitionItem[] { testDefinitionItem }, contentItem, deviceId);
                    combination = new TestCombination(pageData.MvTest.Combination, testSet);
                  }
                }
              }
            }
          }

          var personalizationEvent = new PersonalizationEvent(pageData.DateTime.ToUniversalTime())
          {
            ExposedRules = pageData.PersonalizationData.ExposedRules.Select(r => new Sitecore.ContentTesting.Model.xConnect.PersonalizationRuleData
            {
              RuleId = r.RuleId.Guid,
              RuleSetId = r.RuleSetId.Guid,
              IsOriginal = IsOriginal(testSet, combination, r.RuleSetId.Guid)
            }).ToList(),
            ParentEventId = parentPage.Id
          };

          args.XConnectInteraction.Events.Add(personalizationEvent);
        }
      }
    }
    private static bool IsOriginal(TestSet testSet, TestCombination combination, Guid roleSetId)
    {
      if (testSet == null || combination == null) return false;
      var testVariable = testSet.Variables.First(x => x.Id.Equals(roleSetId));
      var ruleValue = combination.GetValue(roleSetId);
      return ContentTestingFactory.Instance.TestValueInspector.IsOriginalTestValue(testVariable, ruleValue);
    }
  }
}