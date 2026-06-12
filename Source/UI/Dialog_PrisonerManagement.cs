using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using RimPrison.PrisonLabor;

namespace RimPrison.UI
{
    // [UNREVIEWED] Haven't reviewed carefully
    public class Dialog_PrisonerManagement : Window
    {
        private int curTab;
        private List<TabRecord> tabs;
        private PrisonerGroupManager groupManager;

        // Scroll state per tab
        private Vector2 workScrollPos;
        private Vector2 schedScrollPos;
        private Vector2 prisonerScrollPos;
        private Vector2 detailScrollPos;
        private Vector2 overviewScrollPos;

        private string selectedPawnThingId;

        // Cached filtered work types (no Warden, no Hunting)
        private static List<WorkTypeDef> cachedWorkTypes;

        private const float TabVisualHeight = 35f;
        private const float WorkCellSize = 25f;
        private const float WorkHeaderHeight = 22f;
        private const float WorkLabelWidth = 160f;
        private const float WorkColPadding = 8f;
        private const float SchedLabelWidth = 120f;
        private const float SchedSelectorHeight = 65f;
        private const float PrisonerRowHeight = 48f;
        private const float GroupButtonWidth = 140f;
        private const float PortraitSize = 40f;

        private List<Pawn> PawnList =>
            Find.CurrentMap.mapPawns.PrisonersOfColony
                .Where(p => p.IsLaborEnabled()).ToList();

        public Dialog_PrisonerManagement()
        {
            forcePause = false;
            doCloseX = true;
            doCloseButton = false;
            closeOnClickedOutside = true;
            draggable = false;
            absorbInputAroundWindow = true;
            resizeable = true;
            layer = WindowLayer.Dialog;
        }

        public override Vector2 InitialSize => new Vector2(1400f, 750f);

        public override void PostOpen()
        {
            base.PostOpen();
            BuildTabs();
            groupManager = Find.CurrentMap.GetComponent<PrisonerGroupManager>();
            AutoAssignAgeGroups();
        }

        // Yeah...auto assign group here. Not sure if it's elegant
        void AutoAssignAgeGroups()
        {
            if (groupManager == null) return;

            foreach (var pawn in Find.CurrentMap.mapPawns.PrisonersOfColony)
            {
                if (!pawn.IsLaborEnabled()) continue;
                if (groupManager.GetGroupFor(pawn) != null) continue;

                string groupName = pawn.DevelopmentalStage switch
                {
                    var s when s.Baby()  => "RimPrison.GroupBaby".Translate(),
                    var s when s.Child() => "RimPrison.GroupChild".Translate(),
                    _                     => "RimPrison.GroupAdult".Translate()
                };

                var group = groupManager.groups.Find(g => g.name == groupName);
                if (group == null)
                {
                    group = new PrisonerGroup { name = groupName };
                    group.InitDefaults();
                    groupManager.groups.Add(group);
                }

                groupManager.SetGroup(pawn, group);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Rect tabBaseRect = new Rect(inRect.x, inRect.y + 32f,
                inRect.width, inRect.height - 32f);
            Rect tabBgRect = new Rect(inRect.x, inRect.y, inRect.width, TabVisualHeight);
            Widgets.DrawMenuSection(tabBgRect);
            TabDrawer.DrawTabs(tabBaseRect, tabs);

            // Make only the tab bar draggable so drag-painting works in content area.
            // Tabs are drawn at y=-32f to y=35f (group coords); cover the full zone.
            Rect dragRect = new Rect(inRect.x, inRect.y - 32f, inRect.width, 32f + TabVisualHeight);
            GUI.DragWindow(dragRect);

            Rect contentRect = new Rect(
                inRect.x, inRect.y + TabVisualHeight,
                inRect.width, inRect.height - TabVisualHeight);

            if (curTab == 0)
                DrawScheduleTab(contentRect);
            else if (curTab == 1)
                DrawWorkTab(contentRect);
            else if (curTab == 2)
                DrawPrisonerManageTab(contentRect);
            else if (curTab == 3)
                DrawPoliciesTab(contentRect);
            else if (curTab == 4)
                DrawOverviewTab(contentRect);
            else
                DrawSettingsTab(contentRect);
        }

        // ======== Schedule tab (group-level) ========

        private void DrawScheduleTab(Rect rect)
        {
            if (groupManager == null)
            {
                groupManager = Find.CurrentMap.GetComponent<PrisonerGroupManager>();
                if (groupManager == null) return;
            }

            var groups = groupManager.groups;
            if (groups.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimPrison.NoGroups".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            // Time assignment selector at top (vanilla)
            TimeAssignmentSelector.DrawTimeAssignmentSelectorGrid(
                new Rect(rect.x, rect.y, 191f, SchedSelectorHeight));

            // Header: hour numbers
            float gridY = rect.y + SchedSelectorHeight + 4f;
            // Narrower cells (2/3 width), integer pixel math avoids floating point gaps
            float schedTotalW = (rect.width - SchedLabelWidth - 16f) * 2f / 3f;

            Rect hourHeaderRect = new Rect(rect.x + SchedLabelWidth, gridY,
                schedTotalW, 16f);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            for (int h = 0; h < 24; h++)
            {
                float xLeft = Mathf.FloorToInt(h * schedTotalW / 24f);
                float xRight = Mathf.FloorToInt((h + 1) * schedTotalW / 24f);
                Rect hr = new Rect(hourHeaderRect.x + xLeft, hourHeaderRect.y,
                    xRight - xLeft, hourHeaderRect.height);
                Widgets.Label(hr, h.ToString());
            }
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            gridY += 18f;

            // Group rows
            float rowHeight = 24f;
            float viewHeight = groups.Count * rowHeight;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, viewHeight);
            Rect scrollRect = new Rect(rect.x, gridY, rect.width,
                rect.height - (gridY - rect.y));

            Widgets.BeginScrollView(scrollRect, ref schedScrollPos, viewRect);

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                float y = g * rowHeight;

                // Group label
                Rect labelRect = new Rect(0f, y, SchedLabelWidth, rowHeight);
                Widgets.Label(labelRect, group.name);

                // 24 hour blocks — integer pixel math to eliminate gaps
                for (int h = 0; h < 24; h++)
                {
                    float xLeft = Mathf.FloorToInt(h * schedTotalW / 24f);
                    float xRight = Mathf.FloorToInt((h + 1) * schedTotalW / 24f);
                    Rect cellRect = new Rect(SchedLabelWidth + xLeft + 1f, y + 1f,
                        xRight - xLeft - 2f, rowHeight - 2f);
                    DrawSchedCell(cellRect, group, h);
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawSchedCell(Rect rect, PrisonerGroup group, int hour)
        {
            TimeAssignmentDef assignment = group.GetAssignment(hour);
            GUI.DrawTexture(rect, assignment.ColorTexture);

            if (!Mouse.IsOver(rect))
                return;

            Widgets.DrawBox(rect, 2);

            if (Input.GetMouseButton(0) &&
                TimeAssignmentSelector.selectedAssignment != null &&
                assignment != TimeAssignmentSelector.selectedAssignment)
            {
                group.SetAssignment(hour, TimeAssignmentSelector.selectedAssignment);
                groupManager.SyncGroupToAllPawns(group);
                SoundDefOf.Designate_DragStandard_Changed_NoCam.PlayOneShotOnCamera();
            }
        }

        // ======== Work tab (group-level) ========

        private void DrawWorkTab(Rect rect)
        {
            if (groupManager == null)
            {
                groupManager = Find.CurrentMap.GetComponent<PrisonerGroupManager>();
                if (groupManager == null) return;
            }

            var groups = groupManager.groups;
            var workTypes = GetFilteredWorkTypes();

            if (groups.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimPrison.NoGroups".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float rowHeaderW = 140f;
            float colWidth = 68f;
            float headerH = 54f;
            float rowH = 28f;
            float canvasW = Math.Max(rect.width - 16f, rowHeaderW + workTypes.Count * colWidth);
            float canvasH = Math.Max(rect.height, headerH + groups.Count * rowH);
            Rect viewRect = new Rect(0f, 0f, canvasW, canvasH);
            Widgets.BeginScrollView(rect, ref workScrollPos, viewRect);

            // Column headers
            for (int w = 0; w < workTypes.Count; w++)
            {
                Rect hdr = new Rect(rowHeaderW + w * colWidth, 0f, colWidth - 1f, headerH);
                Widgets.DrawBoxSolid(hdr, new Color(0.16f, 0.16f, 0.18f, 0.92f));
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                var wt = workTypes[w];
                Widgets.Label(new Rect(hdr.x, hdr.y + 2f, hdr.width, 18f),
                    wt.labelShort.CapitalizeFirst());
                Widgets.DrawBoxSolid(new Rect(hdr.x + 6f, hdr.y + 21f, hdr.width - 12f, 1f),
                    new Color(1f, 1f, 1f, 0.12f));
                // Wage display below separator
                float wage = RimPrisonMod.Settings.GetWorkTypeWage(wt.defName);
                GUI.color = new Color(0.72f, 0.78f, 0.7f, 0.9f);
                Widgets.Label(new Rect(hdr.x, hdr.y + 24f, hdr.width, 18f),
                    wage.ToString("F1"));
                GUI.color = Color.white;
                // Click header to edit wage
                if (Widgets.ButtonInvisible(hdr))
                {
                    Find.WindowStack.Add(new Dialog_SetWage(wt));
                }
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.UpperLeft;
            }

            // Group rows
            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                float y = headerH + g * rowH;
                Widgets.DrawBoxSolid(new Rect(0f, y, canvasW, rowH - 1f),
                    g % 2 == 0 ? new Color(1f, 1f, 1f, 0.02f) : new Color(0f, 0f, 0f, 0.06f));

                Rect labelRect = new Rect(4f, y + 2f, rowHeaderW - 8f, rowH - 4f);
                Widgets.Label(labelRect, group.name);

                for (int w = 0; w < workTypes.Count; w++)
                {
                    Rect cell = new Rect(rowHeaderW + w * colWidth + 2f, y + 2f,
                        colWidth - 4f, rowH - 4f);
                    DrawWorkCell(cell, group, workTypes[w]);
                }
            }
            Widgets.EndScrollView();
        }

        private static readonly Color[] PriorityColors = {
            new Color(0.22f, 0.22f, 0.24f, 0.6f),  // unused
            new Color(0.2f, 0.45f, 0.28f, 0.78f),   // 1 - high
            new Color(0.25f, 0.38f, 0.52f, 0.78f),   // 2
            new Color(0.35f, 0.31f, 0.22f, 0.78f),   // 3
            new Color(0.42f, 0.2f, 0.2f, 0.72f),     // 4 - low
        };

        private void DrawWorkCell(Rect rect, PrisonerGroup group, WorkTypeDef wt)
        {
            int priority = group.GetPriority(wt);
            Color bg = priority >= 0 && priority < PriorityColors.Length
                ? PriorityColors[priority]
                : new Color(0.22f, 0.22f, 0.24f, 0.6f);

            Widgets.DrawBoxSolid(rect, priority > 0 ? bg : new Color(1f, 1f, 1f, 0.04f));
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.Label(rect, priority > 0 ? priority.ToString() : "-");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            if (!Mouse.IsOver(rect) || Event.current.type != EventType.MouseDown)
                return;

            if (Event.current.button == 0)
            {
                int next = priority - 1;
                if (next < 0) next = 4;
                group.SetPriority(wt, next);
                groupManager.SyncWorkPriority(group, wt, next);
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                Event.current.Use();
            }
            else if (Event.current.button == 1)
            {
                int prev = priority + 1;
                if (prev > 4) prev = 0;
                group.SetPriority(wt, prev);
                groupManager.SyncWorkPriority(group, wt, prev);
                SoundDefOf.DragSlider.PlayOneShotOnCamera();
                Event.current.Use();
            }
        }

        // ======== Prisoner management tab ========

        private void DrawPrisonerManageTab(Rect rect)
        {
            var pawns = PawnList;
            if (pawns.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimPrison.NoLaborPrisoners".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float listWidth = 440f;
            float gap = 12f;
            float detailWidth = rect.width - listWidth - gap;

            // Left: prisoner list
            Rect listRect = new Rect(rect.x, rect.y, listWidth, rect.height);
            float viewHeight = pawns.Count * PrisonerRowHeight;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, viewHeight);
            Widgets.BeginScrollView(listRect, ref prisonerScrollPos, viewRect);

            float y = 0f;
            for (int i = 0; i < pawns.Count; i++)
            {
                Rect rowRect = new Rect(0f, y, viewRect.width, PrisonerRowHeight);
                bool selected = pawns[i].ThingID == selectedPawnThingId;
                if (selected)
                    Widgets.DrawHighlight(rowRect);
                else if (i % 2 == 1)
                    Widgets.DrawLightHighlight(rowRect);

                bool clicked;
                DrawPrisonerRow(rowRect, pawns[i], out clicked);
                if (clicked)
                    selectedPawnThingId = pawns[i].ThingID;
                y += PrisonerRowHeight;
            }

            Widgets.EndScrollView();

            // Right: detail panel
            Rect detailRect = new Rect(listRect.xMax + gap, rect.y, detailWidth, rect.height);
            var selectedPawn = pawns.FirstOrDefault(p => p.ThingID == selectedPawnThingId);
            if (selectedPawn != null)
                DrawPrisonerDetailPanel(detailRect, selectedPawn);
            else
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(detailRect, "RimPrison.SelectPrisoner".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
            }
        }

        private void DrawPrisonerRow(Rect rect, Pawn pawn, out bool clicked)
        {
            float midY = rect.y + (rect.height - PortraitSize) / 2f;

            // Portrait
            Rect portraitRect = new Rect(rect.x + 4f, midY, PortraitSize, PortraitSize);
            var portrait = PortraitsCache.Get(pawn, new Vector2(PortraitSize, PortraitSize), Rot4.South);
            GUI.DrawTexture(portraitRect, portrait);

            // Name
            float nameX = portraitRect.xMax + 6f;
            float nameWidth = 110f;
            float nameY = rect.y + 4f;
            Widgets.Label(new Rect(nameX, nameY, nameWidth, 20f), pawn.LabelShortCap);

            // Age / stage
            Text.Font = GameFont.Tiny;
            string stageLabel = pawn.DevelopmentalStage.Baby() ? "Baby"
                : pawn.DevelopmentalStage.Child() ? "Child" : "Adult";
            Widgets.Label(new Rect(nameX, nameY + 20f, nameWidth, 16f),
                pawn.ageTracker.AgeBiologicalYears + " · " + stageLabel);
            Text.Font = GameFont.Small;

            // Group dropdown
            float groupX = nameX + nameWidth + 4f;
            float groupW = 100f;
            Rect groupRect = new Rect(groupX, rect.y + (rect.height - 22f) / 2f, groupW, 22f);
            DrawGroupButton(groupRect, pawn);

            // Balance + grant
            var comp = pawn.TryGetComp<CompWorkTracker>();
            int coupons = comp?.earnedCoupons ?? 0;
            float balX = groupX + groupW + 6f;
            Widgets.Label(new Rect(balX, midY + 2f, 80f, 22f),
                RimPrisonMod.Settings.WorkCouponName + ":" + coupons);

            float grantBtnW = 40f;
            Rect grantRect = new Rect(balX + 80f, rect.y + (rect.height - 22f) / 2f, grantBtnW, 22f);
            if (RPR_UiStyle.DrawColoredButton(grantRect, "+"))
            {
                Find.WindowStack.Add(new Dialog_GrantCoupons(pawn));
            }

            clicked = Widgets.ButtonInvisible(rect);
        }

        private void DrawPrisonerDetailPanel(Rect rect, Pawn pawn)
        {
            RPR_UiStyle.DrawSubPanel(rect);
            var inner = rect.ContractedBy(12f);
            float detailPortraitSize = 96f;

            // Portrait
            Rect portraitRect = new Rect(inner.x, inner.y, detailPortraitSize, detailPortraitSize);
            var portrait = PortraitsCache.Get(pawn, new Vector2(detailPortraitSize, detailPortraitSize), Rot4.South);
            GUI.DrawTexture(portraitRect, portrait);

            // Info line
            float infoX = portraitRect.xMax + 12f;
            float infoWidth = inner.width - detailPortraitSize - 12f;
            string currentActivity;
            if (pawn.CurJob?.def?.reportString != null)
                currentActivity = pawn.CurJob.def.reportString.Formatted(pawn.Named("PAWN")).Resolve();
            else if (pawn.jobs?.curDriver?.asleep == true)
                currentActivity = "RimPrison.ActivitySleeping".Translate();
            else
                currentActivity = "RimPrison.ActivityIdle".Translate();

            string race = pawn.def?.label ?? "?";
            string infoLine = $"{pawn.LabelShortCap} / {race} / {pawn.ageTracker.AgeBiologicalYears} / {currentActivity}";
            Widgets.Label(new Rect(infoX, inner.y, infoWidth, 20f), infoLine);

            // Balance
            var tracker = pawn.TryGetComp<CompWorkTracker>();
            int balance = tracker?.earnedCoupons ?? 0;
            Widgets.Label(new Rect(infoX, inner.y + 24f, infoWidth, 20f),
                "RimPrison.BalanceLabel".Translate(balance));

            // Group
            if (groupManager == null) groupManager = Find.CurrentMap.GetComponent<PrisonerGroupManager>();
            var group = groupManager?.GetGroupFor(pawn);
            string groupLabel = group != null ? group.name : "RimPrison.NoGroup".Translate();
            Widgets.Label(new Rect(infoX, inner.y + 48f, infoWidth, 20f),
                "RimPrison.GroupLabel".Translate(groupLabel));

            // Thoughts section
            float thoughtsY = portraitRect.yMax + 16f;
            RPR_UiStyle.DrawSectionTitle(new Rect(inner.x, thoughtsY, inner.width, 20f), "RimPrison.ThoughtsTitle".Translate());

            var prisonComp = pawn.TryGetComp<CompPrisonPawn>();
            var thoughts = prisonComp?.thoughts;
            float thoughtsListY = thoughtsY + 24f;
            float thoughtsHeight = inner.yMax - thoughtsListY;

            if (thoughts == null || thoughts.Count == 0)
            {
                Widgets.Label(new Rect(inner.x, thoughtsListY, inner.width, 20f), "RimPrison.ThoughtsEmpty".Translate());
            }
            else
            {
                var thoughtLines = new List<string>(thoughts);
                thoughtLines.Reverse(); // newest at top
                float lineHeight = 22f;
                float totalThoughtsHeight = thoughtLines.Count * lineHeight;
                Rect thoughtsViewRect = new Rect(0f, 0f, inner.width - 16f, totalThoughtsHeight);
                Widgets.BeginScrollView(new Rect(inner.x, thoughtsListY, inner.width, thoughtsHeight), ref detailScrollPos, thoughtsViewRect);
                float ty = 0f;
                Text.Font = GameFont.Tiny;
                for (int i = 0; i < thoughtLines.Count; i++)
                {
                    Widgets.Label(new Rect(0f, ty, thoughtsViewRect.width, lineHeight), "· " + thoughtLines[i]);
                    ty += lineHeight;
                }
                Text.Font = GameFont.Small;
                Widgets.EndScrollView();
            }
        }

        private void DrawGroupButton(Rect rect, Pawn pawn)
        {
            if (groupManager == null)
                groupManager = Find.CurrentMap.GetComponent<PrisonerGroupManager>();

            var currentGroup = groupManager?.GetGroupFor(pawn);
            string label = currentGroup != null
                ? currentGroup.name
                : "RimPrison.NoGroup".Translate();

            if (!RPR_UiStyle.DrawColoredButton(rect, label))
                return;

            List<FloatMenuOption> options = new List<FloatMenuOption>();

            options.Add(new FloatMenuOption("RimPrison.NoGroup".Translate(), delegate
            {
                groupManager?.RemoveFromAllGroups(pawn);
            }));

            if (groupManager != null)
            {
                for (int i = 0; i < groupManager.groups.Count; i++)
                {
                    var g = groupManager.groups[i];
                    options.Add(new FloatMenuOption(g.name, delegate
                    {
                        groupManager.SetGroup(pawn, g);
                    }));
                }
            }

            options.Add(new FloatMenuOption("RimPrison.ManageGroups".Translate(), delegate
            {
                if (groupManager != null)
                    Find.WindowStack.Add(new Dialog_ManagePrisonerGroups(groupManager));
            }));

            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ======== Policies tab (outfit + drug, group-level) ========

        private const float PolicyRowHeight = 30f;
        private const float PolicyLabelWidth = 160f;

        private void DrawPoliciesTab(Rect rect)
        {
            if (groupManager == null)
            {
                groupManager = Find.CurrentMap.GetComponent<PrisonerGroupManager>();
                if (groupManager == null) return;
            }

            var groups = groupManager.groups;
            if (groups.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "RimPrison.NoGroups".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float manageBtnW = 44f;
            float innerGap = 4f;       // between dropdown and its manage button
            float colSpacing = 16f;    // between column pairs
            float headerH = 24f;

            // Three equal columns with spacing between them
            float colAreaW = (rect.width - PolicyLabelWidth - 16f - colSpacing * 2f) / 3f;
            float dropdownW = colAreaW - manageBtnW - innerGap;
            // pairW = dropdownW + innerGap + manageBtnW = colAreaW exactly

            float col0X = PolicyLabelWidth;
            float col1X = PolicyLabelWidth + colAreaW + colSpacing;
            float col2X = PolicyLabelWidth + (colAreaW + colSpacing) * 2f;

            // Column headers
            float headerY = rect.y;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(col0X, headerY, colAreaW, headerH),
                "RimPrison.ApparelManagement".Translate());
            Widgets.Label(new Rect(col1X, headerY, colAreaW, headerH),
                "RimPrison.DrugManagement".Translate());
            Widgets.Label(new Rect(col2X, headerY, colAreaW, headerH),
                "RimPrison.FoodManagement".Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            float contentY = headerY + headerH + 4f;
            float viewHeight = groups.Count * PolicyRowHeight + contentY;
            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Math.Max(viewHeight, rect.height));
            Vector2 scrollPos = Vector2.zero;
            Widgets.BeginScrollView(rect, ref scrollPos, viewRect);

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                float y = contentY + g * PolicyRowHeight;

                Rect labelRect = new Rect(0f, y + 4f, PolicyLabelWidth, PolicyRowHeight);
                Widgets.Label(labelRect, group.name);

                // --- Outfit column ---
                Rect outfitRect = new Rect(col0X, y + 2f, dropdownW, PolicyRowHeight - 4f);
                string outfitLabel = group.apparelPolicy != null
                    ? group.apparelPolicy.label
                    : "RimPrison.None".Translate();
                if (RPR_UiStyle.DrawColoredButton(outfitRect, outfitLabel))
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    options.Add(new FloatMenuOption("RimPrison.None".Translate(), delegate
                    {
                        group.apparelPolicy = null;
                        groupManager.SyncOutfitPolicy(group);
                    }));
                    foreach (var policy in Current.Game.outfitDatabase.AllOutfits)
                    {
                        var captured = policy;
                        options.Add(new FloatMenuOption(policy.label, delegate
                        {
                            group.apparelPolicy = captured;
                            groupManager.SyncOutfitPolicy(group);
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }

                Rect outfitManageRect = new Rect(col0X + dropdownW + innerGap, y + 2f,
                    manageBtnW, PolicyRowHeight - 4f);
                if (RPR_UiStyle.DrawColoredButton(outfitManageRect, "RimPrison.Manage".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_ManageApparelPolicies(group.apparelPolicy));
                }

                // --- Drug column ---
                Rect drugRect = new Rect(col1X, y + 2f, dropdownW, PolicyRowHeight - 4f);
                string drugLabel = group.drugPolicy != null
                    ? group.drugPolicy.label
                    : "RimPrison.None".Translate();
                if (RPR_UiStyle.DrawColoredButton(drugRect, drugLabel))
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    options.Add(new FloatMenuOption("RimPrison.None".Translate(), delegate
                    {
                        group.drugPolicy = null;
                        groupManager.SyncDrugPolicy(group);
                    }));
                    foreach (var policy in Current.Game.drugPolicyDatabase.AllPolicies)
                    {
                        var captured = policy;
                        options.Add(new FloatMenuOption(policy.label, delegate
                        {
                            group.drugPolicy = captured;
                            groupManager.SyncDrugPolicy(group);
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }

                Rect drugManageRect = new Rect(col1X + dropdownW + innerGap, y + 2f,
                    manageBtnW, PolicyRowHeight - 4f);
                if (RPR_UiStyle.DrawColoredButton(drugManageRect, "RimPrison.Manage".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_ManageDrugPolicies(group.drugPolicy));
                }

                // --- Food column ---
                Rect foodRect = new Rect(col2X, y + 2f, dropdownW, PolicyRowHeight - 4f);
                string foodLabel = group.foodRestriction != null
                    ? group.foodRestriction.label
                    : "RimPrison.None".Translate();
                if (RPR_UiStyle.DrawColoredButton(foodRect, foodLabel))
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    options.Add(new FloatMenuOption("RimPrison.None".Translate(), delegate
                    {
                        group.foodRestriction = null;
                        groupManager.SyncFoodRestriction(group);
                    }));
                    foreach (var policy in Current.Game.foodRestrictionDatabase.AllFoodRestrictions)
                    {
                        var captured = policy;
                        options.Add(new FloatMenuOption(policy.label, delegate
                        {
                            group.foodRestriction = captured;
                            groupManager.SyncFoodRestriction(group);
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }

                Rect foodManageRect = new Rect(col2X + dropdownW + innerGap, y + 2f,
                    manageBtnW, PolicyRowHeight - 4f);
                if (RPR_UiStyle.DrawColoredButton(foodManageRect, "RimPrison.Manage".Translate()))
                {
                    Find.WindowStack.Add(new Dialog_ManageFoodPolicies(group.foodRestriction));
                }
            }

            Widgets.EndScrollView();
        }

        // ======== Overview tab ========

        private void DrawOverviewTab(Rect rect)
        {
            if (groupManager == null)
            {
                groupManager = Find.CurrentMap.GetComponent<PrisonerGroupManager>();
                if (groupManager == null) return;
            }

            var map = Find.CurrentMap;
            var prisoners = map.mapPawns.PrisonersOfColony;
            int adultCount = 0, childCount = 0, babyCount = 0;
            foreach (var p in prisoners)
            {
                if (!p.IsLaborEnabled()) continue;
                if (p.DevelopmentalStage.Baby()) babyCount++;
                else if (p.DevelopmentalStage.Child()) childCount++;
                else adultCount++;
            }
            int total = adultCount + childCount + babyCount;

            float colW = (rect.width - 24f) / 2f;
            float rowH = 210f;
            float gap = 12f;

            // Population card
            Rect popRect = new Rect(rect.x, rect.y, colW, rowH);
            RPR_UiStyle.DrawSubPanel(popRect);
            var popInner = popRect.ContractedBy(10f);
            RPR_UiStyle.DrawSectionTitle(new Rect(popInner.x, popInner.y, popInner.width, 22f), "RimPrison.Population".Translate());
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(popInner.x, popInner.y + 28f, popInner.width, 36f), total.ToString());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(popInner.x, popInner.y + 70f, popInner.width, 22f), "RimPrison.AdultCount".Translate(adultCount.ToString()));
            Widgets.Label(new Rect(popInner.x, popInner.y + 96f, popInner.width, 22f), "RimPrison.ChildCount".Translate(childCount.ToString()));
            Widgets.Label(new Rect(popInner.x, popInner.y + 122f, popInner.width, 22f), "RimPrison.BabyCount".Translate(babyCount.ToString()));

            // Suppression card: ring on left, factors on right
            Rect suppRect = new Rect(rect.x + colW + gap, rect.y, colW, rowH);
            RPR_UiStyle.DrawSubPanel(suppRect);
            var suppInner = suppRect.ContractedBy(10f);
            RPR_UiStyle.DrawSectionTitle(new Rect(suppInner.x, suppInner.y, suppInner.width, 22f), "RimPrison.Suppression".Translate());

            var suppComp = map?.GetComponent<GameComponent_Suppression>();
            float colonySupp = suppComp?.colonySuppression ?? 50f;
            float ringSize = 110f;
            float ringX = suppInner.x + 8f;
            Rect ringRect = new Rect(ringX, suppInner.y + 28f, ringSize, ringSize);
            RPR_UiStyle.DrawSuppressionRing(ringRect, colonySupp / 100f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(ringX, suppInner.y + ringSize + 28f, ringSize, 22f),
                "RimPrison.SuppressionValue".Translate(colonySupp.ToString("F0")));
            Text.Anchor = TextAnchor.UpperLeft;

            // Factor contributions on the right
            float factorX = ringX + ringSize + 16f;
            float factorColW = (suppInner.xMax - factorX) * 0.55f;
            float statusX = factorX + factorColW + 8f;
            float statusW = suppInner.xMax - statusX;
            Text.Font = GameFont.Tiny;
            float factorY = suppInner.y + 28f;
            int colonists = map.mapPawns.FreeColonistsSpawnedCount;
            int turrets = SuppressionCalculator.CountTurretsInPrisonArea(map);
            float effective = SuppressionCalculator.CalculateEffectivePrisonerCount(map);
            float diffVal = Find.Storyteller?.difficulty?.threatScale ?? 1f;
            float avgMood = 0f, avgHealth = 0f;
            int count = 0;
            foreach (var p in map.mapPawns.PrisonersOfColony)
            {
                if (!p.IsLaborEnabled()) continue;
                avgMood += p.needs?.mood?.CurLevelPercentage ?? 0.5f;
                avgHealth += p.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
                count++;
            }
            if (count > 0) { avgMood /= count; avgHealth /= count; }
            else { avgMood = 0.5f; avgHealth = 1f; }

            var bd = SuppressionCalculator.CalculateSuppression(
                effective, guardCount: 0, colonists, turrets,
                avgMood, avgHealth, SuppressionCalculator.CurrentRegime, diffVal);

            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorBase", 50f);
            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorTurrets", bd.turretFactor);
            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorColonists", bd.guardFactor);
            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorPrisoners", bd.prisonerFactor);
            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorMood", bd.moodFactor);
            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorHealth", bd.healthFactor);
            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorRegime", bd.regimeMod);
            DrawFactorLine(factorX, ref factorY, factorColW, "RimPrison.SuppFactorDifficulty", bd.difficultyMod);

            // Break status on the right side of factors
            float statusY = suppInner.y + 28f;
            bool breakAllowed = colonySupp < 50f;
            bool pbAllowed = colonySupp < 30f;
            GUI.color = breakAllowed ? new Color(0.8f, 0.3f, 0.3f) : new Color(0.3f, 0.8f, 0.3f);
            Widgets.Label(new Rect(statusX, statusY, statusW, 20f),
                breakAllowed ? "RimPrison.SuppBreakAllowed".Translate() : "RimPrison.SuppBreakBlocked".Translate());
            statusY += 20f;
            GUI.color = pbAllowed ? new Color(0.8f, 0.3f, 0.3f) : new Color(0.3f, 0.8f, 0.3f);
            Widgets.Label(new Rect(statusX, statusY, statusW, 20f),
                pbAllowed ? "RimPrison.SuppPrisonBreakAllowed".Translate() : "RimPrison.SuppPrisonBreakBlocked".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Activity log — full width below the top row
            float secondRowY = rect.y + rowH + gap;
            float secondRowH = rect.height - rowH - gap;
            Rect logRect = new Rect(rect.x, secondRowY, rect.width, secondRowH);
            RPR_UiStyle.DrawSubPanel(logRect);
            var logInner = logRect.ContractedBy(10f);
            RPR_UiStyle.DrawSectionTitle(new Rect(logInner.x, logInner.y, logInner.width, 22f), "RimPrison.ActivityLog".Translate());

            var logComp = map?.GetComponent<GameComponent_ActivityLog>();
            if (logComp == null || logComp.entries.Count == 0)
            {
                RPR_UiStyle.DrawMutedLabel(new Rect(logInner.x, logInner.y + 32f, logInner.width, 40f), "RimPrison.ActivityLogEmpty".Translate());
            }
            else
            {
                int showCount = System.Math.Min(logComp.entries.Count, 30);
                float rowH2 = 20f;
                float listY = logInner.y + 28f;
                float viewH = showCount * rowH2;
                Rect listRect = new Rect(logInner.x, listY, logInner.width, logInner.height - 28f);
                Rect viewRect2 = new Rect(0f, 0f, listRect.width - 16f, viewH);
                Widgets.BeginScrollView(listRect, ref overviewScrollPos, viewRect2);
                Text.Font = GameFont.Tiny;
                int startIdx = logComp.entries.Count - showCount;
                for (int i = 0; i < showCount; i++)
                {
                    var entry = logComp.entries[startIdx + i];
                    Rect rowRect = new Rect(0f, i * rowH2, viewRect2.width, rowH2);
                    if (i % 2 == 0)
                        Widgets.DrawLightHighlight(rowRect);
                    Widgets.Label(rowRect, entry.Format());
                }
                Text.Font = GameFont.Small;
                Widgets.EndScrollView();
            }
        }

        // ======== Settings tab ========

        private void DrawSettingsTab(Rect rect)
        {
            // [TODO] NO LOGIC — most settings not implemented yet
            RPR_UiStyle.DrawSubPanel(rect);
            var inner = rect.ContractedBy(12f);
            RPR_UiStyle.DrawSectionTitle(new Rect(inner.x, inner.y, inner.width, 24f), "RimPrison.Settings".Translate());

            float y = inner.y + 32f;
            float colW = (inner.width - 12f) / 2f;

            // Left column — currency name (wired up to our settings)
            Widgets.Label(new Rect(inner.x, y, 140f, 24f), "RimPrison.CurrencyName".Translate());
            string currencyBuf = RimPrisonMod.Settings.WorkCouponName;
            currencyBuf = Widgets.TextField(new Rect(inner.x + 144f, y, 120f, 24f), currencyBuf);
            if (currencyBuf != RimPrisonMod.Settings.WorkCouponName)
            {
                RimPrisonMod.Settings.WorkCouponName = currencyBuf;
                RimPrisonMod.Settings.Write();
            }

            y += 30f;
            // Max debt — how far into debt prisoners can go for purchases
            Widgets.Label(new Rect(inner.x, y, 140f, 24f), "RimPrison.MaxDebt".Translate());
            string debtBuf = RimPrisonMod.Settings.MaxDebt.ToString();
            debtBuf = Widgets.TextField(new Rect(inner.x + 144f, y, 80f, 24f), debtBuf);
            if (int.TryParse(debtBuf, out int d) && d >= 0 && d <= 99999 && d != RimPrisonMod.Settings.MaxDebt)
            {
                RimPrisonMod.Settings.MaxDebt = d;
                RimPrisonMod.Settings.Write();
            }

            y += 30f;
            // Daily allowance (already implemented)
            Widgets.Label(new Rect(inner.x, y, 140f, 24f), "RimPrison.DailyAllowance".Translate());
            string buf = RimPrisonMod.Settings.DailyAllowance.ToString();
            buf = Widgets.TextField(new Rect(inner.x + 144f, y, 60f, 24f), buf);
            if (int.TryParse(buf, out int val) && val >= 0 && val <= 9999 && val != RimPrisonMod.Settings.DailyAllowance)
            {
                RimPrisonMod.Settings.DailyAllowance = val;
                RimPrisonMod.Settings.Write();
            }

            y += 30f;
            // Daily fee — 床位和管理费
            Widgets.Label(new Rect(inner.x, y, 140f, 24f), "RimPrison.DailyFee".Translate());
            string feeBuf = RimPrisonMod.Settings.DailyFee.ToString();
            feeBuf = Widgets.TextField(new Rect(inner.x + 144f, y, 60f, 24f), feeBuf);
            if (int.TryParse(feeBuf, out int fee) && fee >= 0 && fee <= 9999 && fee != RimPrisonMod.Settings.DailyFee)
            {
                RimPrisonMod.Settings.DailyFee = fee;
                RimPrisonMod.Settings.Write();
            }

            y += 30f;
            // Ransom amount
            Widgets.Label(new Rect(inner.x, y, 140f, 24f), "RimPrison.RansomAmount".Translate());
            string ransomBuf = RimPrisonMod.Settings.RansomAmount.ToString();
            ransomBuf = Widgets.TextField(new Rect(inner.x + 144f, y, 80f, 24f), ransomBuf);
            if (int.TryParse(ransomBuf, out int r) && r >= 0 && r <= 99999 && r != RimPrisonMod.Settings.RansomAmount)
            {
                RimPrisonMod.Settings.RansomAmount = r;
                RimPrisonMod.Settings.Write();
            }

            y += 30f;
            // Regime dropdown
            Widgets.Label(new Rect(inner.x, y, 140f, 24f), "RimPrison.RegimeLabel".Translate());
            string regimeLabel = SuppressionCalculator.CurrentRegime switch
            {
                SuppressionCalculator.Regime.Harsh => "RimPrison.RegimeHarshLabel".Translate(),
                SuppressionCalculator.Regime.Deterrence => "RimPrison.RegimeDeterrenceLabel".Translate(),
                SuppressionCalculator.Regime.Equality => "RimPrison.RegimeEqualityLabel".Translate(),
                _ => "?"
            };
            if (RPR_UiStyle.DrawColoredButton(new Rect(inner.x + 144f, y, 100f, 24f), regimeLabel))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("RimPrison.RegimeHarshLabel".Translate(), () => SetRegime(SuppressionCalculator.Regime.Harsh)),
                    new FloatMenuOption("RimPrison.RegimeDeterrenceLabel".Translate(), () => SetRegime(SuppressionCalculator.Regime.Deterrence)),
                    new FloatMenuOption("RimPrison.RegimeEqualityLabel".Translate(), () => SetRegime(SuppressionCalculator.Regime.Equality))
                };
                Find.WindowStack.Add(new FloatMenu(options));
            }

            y += 30f;
            // Debt harvest threshold
            Widgets.Label(new Rect(inner.x, y, 140f, 24f), "RimPrison.DebtHarvestThreshold".Translate());
            string harvestBuf = RimPrisonMod.Settings.DebtHarvestThreshold.ToString();
            harvestBuf = Widgets.TextField(new Rect(inner.x + 144f, y, 80f, 24f), harvestBuf);
            if (int.TryParse(harvestBuf, out int hv) && hv >= 0 && hv <= 99999 && hv != RimPrisonMod.Settings.DebtHarvestThreshold)
            {
                RimPrisonMod.Settings.DebtHarvestThreshold = hv;
                RimPrisonMod.Settings.Write();
            }

            y += 30f;
            if (RPR_UiStyle.DrawColoredButton(new Rect(inner.x, y, 200f, 30f),
                "RimPrison.ManagePrisonAreaWork".Translate()))
            {
                Find.WindowStack.Add(new Dialog_ManagePrisonAreaWork());
            }

            y += 36f;
            bool allowColonistRecreation = RimPrisonMod.Settings.AllowColonistRecreationInPrisonArea;
            string recreationLabel = "RimPrison.AllowColonistRecreationInPrisonArea".Translate();
            float checkboxSize = 24f;
            float labelWidth = Mathf.Min(Text.CalcSize(recreationLabel).x + 4f, colW - checkboxSize - 8f);
            Widgets.Label(new Rect(inner.x, y, labelWidth, 24f), recreationLabel);
            Widgets.Checkbox(new Vector2(inner.x + labelWidth + 4f, y), ref allowColonistRecreation, checkboxSize);
            if (allowColonistRecreation != RimPrisonMod.Settings.AllowColonistRecreationInPrisonArea)
            {
                RimPrisonMod.Settings.AllowColonistRecreationInPrisonArea = allowColonistRecreation;
                RimPrisonMod.Settings.Write();
            }

            // Right column — global status summary
            float rightCol = inner.x + colW + 12f;
            float rightY = inner.y + 32f;
            RPR_UiStyle.DrawSectionTitle(new Rect(rightCol, rightY, colW, 24f), "RimPrison.GlobalStatus".Translate());

            var map = Find.CurrentMap;
            var prisoners = map.mapPawns.PrisonersOfColony;
            int labCount = 0;
            foreach (var p in prisoners)
            {
                if (p.IsLaborEnabled()) labCount++;
            }

            rightY += 28f;
            Widgets.Label(new Rect(rightCol, rightY, colW, 22f), "RimPrison.TotalPrisoners".Translate(prisoners.Count.ToString()));
            rightY += 24f;
            Widgets.Label(new Rect(rightCol, rightY, colW, 22f), "RimPrison.LaborEnabledCount".Translate(labCount.ToString()));
            rightY += 24f;
            Widgets.Label(new Rect(rightCol, rightY, colW, 22f), "RimPrison.GroupCount".Translate(groupManager.groups.Count.ToString()));
        }

        private static void DrawFactorLine(float x, ref float y, float width, string labelKey, float value)
        {
            string sign = value >= 0f ? "+" : "";
            string text = labelKey.Translate(sign + value.ToString("F1"));
            Widgets.Label(new Rect(x, y, width / 2f, 20f), text);
            y += 20f;
        }

        // ======== Helpers ========

        private static List<WorkTypeDef> GetFilteredWorkTypes()
        {
            if (cachedWorkTypes == null)
            {
                cachedWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                    .Where(wt => wt.defName != "Warden" && wt.defName != "Hunting")
                    .ToList();
            }
            return cachedWorkTypes;
        }

        private void BuildTabs()
        {
            tabs = new List<TabRecord>
            {
                new TabRecord("RimPrison.ScheduleTab".Translate(),
                    delegate { curTab = 0; }, () => curTab == 0),
                new TabRecord("RimPrison.WorkTab".Translate(),
                    delegate { curTab = 1; }, () => curTab == 1),
                new TabRecord("RimPrison.PrisonerManageTab".Translate(),
                    delegate { curTab = 2; }, () => curTab == 2),
                new TabRecord("RimPrison.PoliciesTab".Translate(),
                    delegate { curTab = 3; }, () => curTab == 3),
                new TabRecord("RimPrison.OverviewTab".Translate(),
                    delegate { curTab = 4; }, () => curTab == 4),
                new TabRecord("RimPrison.SettingsTab".Translate(),
                    delegate { curTab = 5; }, () => curTab == 5),
                new TabRecord("RimPrison.HelpTab".Translate(),
                    delegate { Find.WindowStack.Add(new Dialog_RimPrisonHelp()); },
                    () => false)
            };
        }

        private static void SetRegime(SuppressionCalculator.Regime regime)
        {
            var prev = SuppressionCalculator.CurrentRegime;
            SuppressionCalculator.CurrentRegime = regime;
            if (Find.CurrentMap?.GetComponent<GameComponent_Regime>() is { } comp)
            {
                comp.ApplyToAllPrisoners();
            }
            RimPrisonMod.Settings.Write();
        }
    }
}
