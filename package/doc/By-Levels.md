# By Levels

Filters the input elements to those whose level is checked on the node.

The list contains only levels that at least one model element uses, sorted by elevation (low to high). The saved key is the level UniqueId.

Level lookup order, used both to fill the list and to test each element:

1. `Element.LevelId`
2. `FAMILY_LEVEL_PARAM`
3. `SCHEDULE_LEVEL_PARAM`
4. `RBS_START_LEVEL_PARAM`
5. `INSTANCE_REFERENCE_LEVEL_PARAM`

An element with no level is left out. A LevelId that points at a deleted level is ignored.

With nothing checked, the node returns an empty list. Linked-model elements are excluded.
