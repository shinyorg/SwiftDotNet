using Android.App;

// Assembly-level manifest attributes are merged into the consuming app's AndroidManifest.xml <application>.
// Declares the app as an Android Auto app built on the Car App Library templates.
[assembly: MetaData("com.google.android.gms.car.application", Resource = "@xml/automotive_app_desc")]
// The renderer degrades per feature (tabs need car API 6, app-drawn maps API 7), so accept every host API level.
[assembly: MetaData("androidx.car.app.minCarApiLevel", Value = "1")]
